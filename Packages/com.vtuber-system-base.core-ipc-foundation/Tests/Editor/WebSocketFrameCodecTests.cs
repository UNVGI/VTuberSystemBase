#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class WebSocketFrameCodecTests
    {
        private const uint FixedMaskKey = 0xDEADBEEFu;

        private static WebSocketFrameWriter NewWriter(MemoryStream stream, bool maskOutgoing)
        {
            return new WebSocketFrameWriter(stream, maskOutgoing, () => FixedMaskKey);
        }

        private static MemoryStream RewindCopy(MemoryStream source)
        {
            return new MemoryStream(source.ToArray(), writable: false);
        }

        // -- Frame size encoding ----------------------------------------------

        [Test]
        public void EncodeFrame_Unmasked_ShortPayload_TwoByteHeader()
        {
            byte[] payload = Enumerable.Range(0, 5).Select(i => (byte)i).ToArray();
            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                fin: true,
                opcode: WebSocketOpcode.Text,
                payload: payload,
                maskingKey: null);

            Assert.AreEqual(2 + payload.Length, framed.Length);
            Assert.AreEqual(0x81, framed[0]);
            Assert.AreEqual(payload.Length, framed[1] & 0x7F);
            Assert.AreEqual(0, framed[1] & 0x80);
            CollectionAssert.AreEqual(payload, framed.Skip(2).Take(payload.Length));
        }

        [Test]
        public void EncodeFrame_Unmasked_MediumPayload_UsesTwoByteExtendedLength()
        {
            byte[] payload = new byte[200];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                true, WebSocketOpcode.Text, payload, null);

            Assert.AreEqual(0x81, framed[0]);
            Assert.AreEqual(126, framed[1] & 0x7F);
            int extended = (framed[2] << 8) | framed[3];
            Assert.AreEqual(payload.Length, extended);
            Assert.AreEqual(4 + payload.Length, framed.Length);
            CollectionAssert.AreEqual(payload, framed.Skip(4));
        }

        [Test]
        public void EncodeFrame_Unmasked_LargePayload_UsesEightByteExtendedLength()
        {
            byte[] payload = new byte[70_000];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                true, WebSocketOpcode.Text, payload, null);

            Assert.AreEqual(127, framed[1] & 0x7F);
            long extended = 0;
            for (int i = 0; i < 8; i++) extended = (extended << 8) | framed[2 + i];
            Assert.AreEqual(payload.Length, extended);
            Assert.AreEqual(10 + payload.Length, framed.Length);
        }

        [Test]
        public void EncodeFrame_Masked_AppliesMaskAndSetsMaskBit()
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello");
            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                true, WebSocketOpcode.Text, payload, FixedMaskKey);

            Assert.AreEqual(0x81, framed[0]);
            Assert.AreEqual(0x80 | payload.Length, framed[1]);

            byte[] mask = new byte[]
            {
                (byte)(FixedMaskKey >> 24 & 0xFF),
                (byte)(FixedMaskKey >> 16 & 0xFF),
                (byte)(FixedMaskKey >> 8 & 0xFF),
                (byte)(FixedMaskKey & 0xFF),
            };
            CollectionAssert.AreEqual(mask, framed.Skip(2).Take(4));

            byte[] masked = framed.Skip(6).ToArray();
            Assert.AreEqual(payload.Length, masked.Length);
            for (int i = 0; i < payload.Length; i++)
            {
                Assert.AreEqual((byte)(payload[i] ^ mask[i & 3]), masked[i],
                    $"masked byte at index {i} mismatch");
            }
        }

        // -- Roundtrip read/write ---------------------------------------------

        [Test]
        public async Task RoundTrip_Text_ServerToClient_Unmasked()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteTextMessageAsync("hello world", CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);

            var result = await reader.ReadMessageAsync(CancellationToken.None);
            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(WebSocketOpcode.Text, result.Frame.Opcode);
            Assert.IsTrue(result.Frame.Fin);
            Assert.IsFalse(result.Frame.WasMasked);
            Assert.AreEqual("hello world", Encoding.UTF8.GetString(result.Frame.Payload));
        }

        [Test]
        public async Task RoundTrip_Text_ClientToServer_Masked()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: true);
            await writer.WriteTextMessageAsync("こんにちは", CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: true);

            var result = await reader.ReadMessageAsync(CancellationToken.None);
            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(WebSocketOpcode.Text, result.Frame.Opcode);
            Assert.IsTrue(result.Frame.Fin);
            Assert.IsTrue(result.Frame.WasMasked);
            Assert.AreEqual("こんにちは", Encoding.UTF8.GetString(result.Frame.Payload));
        }

        [Test]
        public async Task RoundTrip_Text_LargePayload_OverSixteenBitBoundary()
        {
            string text = new string('A', 70_000);
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteTextMessageAsync(text, CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false,
                maxMessagePayloadBytes: 1_048_576);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(text, Encoding.UTF8.GetString(result.Frame.Payload));
        }

        // -- Fragmentation ----------------------------------------------------

        [Test]
        public async Task Fragmented_Text_AcrossThreeFrames_Reassembles()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteFrameAsync(false, WebSocketOpcode.Text,
                Encoding.UTF8.GetBytes("foo"), CancellationToken.None);
            await writer.WriteFrameAsync(false, WebSocketOpcode.Continuation,
                Encoding.UTF8.GetBytes("bar"), CancellationToken.None);
            await writer.WriteFrameAsync(true, WebSocketOpcode.Continuation,
                Encoding.UTF8.GetBytes("baz"), CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(WebSocketOpcode.Text, result.Frame.Opcode);
            Assert.IsTrue(result.Frame.Fin);
            Assert.AreEqual("foobarbaz", Encoding.UTF8.GetString(result.Frame.Payload));
        }

        [Test]
        public async Task Fragmented_Continuation_WithoutInitialFrame_IsProtocolError()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteFrameAsync(true, WebSocketOpcode.Continuation,
                Encoding.UTF8.GetBytes("oops"), CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("Continuation", result.ErrorMessage);
        }

        [Test]
        public async Task Fragmented_NewDataFrameInsteadOfContinuation_IsProtocolError()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteFrameAsync(false, WebSocketOpcode.Text,
                Encoding.UTF8.GetBytes("part1"), CancellationToken.None);
            await writer.WriteFrameAsync(true, WebSocketOpcode.Text,
                Encoding.UTF8.GetBytes("part2"), CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("continuation", result.ErrorMessage);
        }

        // -- Ping / Pong ------------------------------------------------------

        [Test]
        public async Task RoundTrip_Ping_Unmasked()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            byte[] payload = new byte[] { 1, 2, 3, 4 };
            await writer.WritePingAsync(payload, CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(WebSocketOpcode.Ping, result.Frame.Opcode);
            Assert.IsTrue(result.Frame.Fin);
            CollectionAssert.AreEqual(payload, result.Frame.Payload);
        }

        [Test]
        public async Task RoundTrip_Pong_Masked()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: true);
            byte[] payload = new byte[] { 9, 8, 7, 6, 5 };
            await writer.WritePongAsync(payload, CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: true);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Frame, result.Status);
            Assert.AreEqual(WebSocketOpcode.Pong, result.Frame.Opcode);
            Assert.IsTrue(result.Frame.WasMasked);
            CollectionAssert.AreEqual(payload, result.Frame.Payload);
        }

        // -- Close ------------------------------------------------------------

        [Test]
        public async Task Close_WithCodeAndReason_RoundTripsThroughReadMessage()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteCloseAsync(WebSocketCloseCode.NormalClosure, "bye",
                CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Close, result.Status);
            Assert.AreEqual(WebSocketCloseCode.NormalClosure, result.CloseCode);
            Assert.AreEqual("bye", result.CloseReason);
        }

        [Test]
        public async Task Close_EmptyPayload_RoundTrips()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteFrameAsync(true, WebSocketOpcode.Close,
                ReadOnlyMemory<byte>.Empty, CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.Close, result.Status);
            Assert.IsNull(result.CloseCode);
            Assert.IsNull(result.CloseReason);
        }

        // -- Mask enforcement -------------------------------------------------

        [Test]
        public async Task Server_Reading_Unmasked_FrameFromClient_ReturnsMaskRequired()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteTextMessageAsync("nope", CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: true);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.MaskRequired, result.Status);
        }

        [Test]
        public async Task Client_Reading_Masked_FrameFromServer_ReturnsMaskForbidden()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: true);
            await writer.WriteTextMessageAsync("nope", CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.MaskForbidden, result.Status);
        }

        // -- Size limits ------------------------------------------------------

        [Test]
        public async Task SinglePayload_OverLimit_ReturnsMessageTooBig()
        {
            byte[] payload = new byte[200];
            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                true, WebSocketOpcode.Text, payload, null);

            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false,
                maxMessagePayloadBytes: 100);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.MessageTooBig, result.Status);
            Assert.AreEqual(200, result.ObservedSize);
            Assert.AreEqual(100, result.LimitSize);
        }

        [Test]
        public async Task FragmentedPayload_AccumulatedOverLimit_ReturnsMessageTooBig()
        {
            var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            await writer.WriteFrameAsync(false, WebSocketOpcode.Text,
                new byte[60], CancellationToken.None);
            await writer.WriteFrameAsync(true, WebSocketOpcode.Continuation,
                new byte[60], CancellationToken.None);

            using var reading = RewindCopy(ms);
            var reader = new WebSocketFrameReader(reading, requireMask: false,
                maxMessagePayloadBytes: 100);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.MessageTooBig, result.Status);
            Assert.AreEqual(120, result.ObservedSize);
            Assert.AreEqual(100, result.LimitSize);
        }

        // -- UTF-8 validation -------------------------------------------------

        [Test]
        public async Task TextFrame_WithInvalidUtf8_ReturnsInvalidUtf8()
        {
            byte[] invalid = new byte[] { 0xC3, 0x28 };
            byte[] framed = WebSocketFrameWriter.EncodeFrame(
                true, WebSocketOpcode.Text, invalid, null);

            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadMessageAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.InvalidUtf8, result.Status);
        }

        // -- Header validation ------------------------------------------------

        [Test]
        public async Task UnknownOpcode_ReturnsProtocolError()
        {
            byte[] framed = new byte[] { 0x83, 0x00 };
            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("opcode", result.ErrorMessage);
        }

        [Test]
        public async Task RsvBitsSet_ReturnsProtocolError()
        {
            byte[] framed = new byte[] { 0xC1, 0x00 };
            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("RSV", result.ErrorMessage);
        }

        [Test]
        public async Task ControlFrame_WithFinUnset_ReturnsProtocolError()
        {
            byte[] framed = new byte[] { 0x09, 0x00 };
            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("Control", result.ErrorMessage);
        }

        [Test]
        public async Task ControlFrame_WithOversizedPayload_ReturnsProtocolError()
        {
            byte[] framed = new byte[2 + 2 + 200];
            framed[0] = 0x89;
            framed[1] = 126;
            framed[2] = 0x00;
            framed[3] = 200;
            using var reading = new MemoryStream(framed, writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.ProtocolError, result.Status);
            StringAssert.Contains("Control", result.ErrorMessage);
        }

        [Test]
        public async Task EmptyStream_ReturnsEndOfStream()
        {
            using var reading = new MemoryStream(Array.Empty<byte>(), writable: false);
            var reader = new WebSocketFrameReader(reading, requireMask: false);
            var result = await reader.ReadFrameAsync(CancellationToken.None);

            Assert.AreEqual(WebSocketReadStatus.EndOfStream, result.Status);
        }

        // -- Writer guard rails -----------------------------------------------

        [Test]
        public void Writer_FragmentedControlFrame_Throws()
        {
            using var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await writer.WriteFrameAsync(false, WebSocketOpcode.Ping,
                    Array.Empty<byte>(), CancellationToken.None));
        }

        [Test]
        public void Writer_OversizedControlFrame_Throws()
        {
            using var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            byte[] big = new byte[200];
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await writer.WriteFrameAsync(true, WebSocketOpcode.Pong,
                    big, CancellationToken.None));
        }

        [Test]
        public void Writer_UnknownOpcode_Throws()
        {
            using var ms = new MemoryStream();
            using var writer = NewWriter(ms, maskOutgoing: false);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await writer.WriteFrameAsync(true, (WebSocketOpcode)0x05,
                    Array.Empty<byte>(), CancellationToken.None));
        }

        // -- IsValidUtf8 helper ----------------------------------------------

        [Test]
        public void IsValidUtf8_ValidSequence_ReturnsTrue()
        {
            Assert.IsTrue(WebSocketFrameReader.IsValidUtf8(Encoding.UTF8.GetBytes("hello")));
            Assert.IsTrue(WebSocketFrameReader.IsValidUtf8(Encoding.UTF8.GetBytes("こんにちは")));
            Assert.IsTrue(WebSocketFrameReader.IsValidUtf8(Array.Empty<byte>()));
        }

        [Test]
        public void IsValidUtf8_InvalidSequence_ReturnsFalse()
        {
            Assert.IsFalse(WebSocketFrameReader.IsValidUtf8(new byte[] { 0xC3, 0x28 }));
            Assert.IsFalse(WebSocketFrameReader.IsValidUtf8(new byte[] { 0xFF, 0xFE, 0xFD }));
        }
    }
}
