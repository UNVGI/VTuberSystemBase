#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class HandshakeProcessorTests
    {
        private const string Crlf = "\r\n";

        private static string BuildValidRequest(
            string key = "dGhlIHNhbXBsZSBub25jZQ==",
            string method = "GET",
            string target = "/",
            string version = "HTTP/1.1",
            string host = "localhost:61874",
            string upgrade = "websocket",
            string connection = "Upgrade",
            string secVersion = "13")
        {
            var sb = new StringBuilder();
            sb.Append(method).Append(' ').Append(target).Append(' ').Append(version).Append(Crlf);
            if (host != null) sb.Append("Host: ").Append(host).Append(Crlf);
            if (upgrade != null) sb.Append("Upgrade: ").Append(upgrade).Append(Crlf);
            if (connection != null) sb.Append("Connection: ").Append(connection).Append(Crlf);
            if (key != null) sb.Append("Sec-WebSocket-Key: ").Append(key).Append(Crlf);
            if (secVersion != null) sb.Append("Sec-WebSocket-Version: ").Append(secVersion).Append(Crlf);
            sb.Append(Crlf);
            return sb.ToString();
        }

        // -- ComputeAccept --------------------------------------------------

        [Test]
        public void ComputeAccept_Rfc6455Section_4_2_2_Example_MatchesSpec()
        {
            // RFC 6455 §4.2.2: "dGhlIHNhbXBsZSBub25jZQ==" → "s3pPLMBiTxaQ9kYGzzhZRbK+xOo="
            string accept = HandshakeProcessor.ComputeAccept("dGhlIHNhbXBsZSBub25jZQ==");
            Assert.AreEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", accept);
        }

        [Test]
        public void ComputeAccept_AnotherKnownKey_ReturnsExpectedDigest()
        {
            // Independently computed: SHA-1("x3JJHMbDL1EzLkh9GBhXDw==258EAFA5-E914-47DA-95CA-C5AB0DC85B11")
            // = HSmrc0sMlYUkAGmm5OPpG2HaGWk= (well-known second example reused in MDN docs).
            string accept = HandshakeProcessor.ComputeAccept("x3JJHMbDL1EzLkh9GBhXDw==");
            Assert.AreEqual("HSmrc0sMlYUkAGmm5OPpG2HaGWk=", accept);
        }

        [Test]
        public void ComputeAccept_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => HandshakeProcessor.ComputeAccept(null!));
        }

        // -- ParseRequest: success path -------------------------------------

        [Test]
        public void ParseRequest_ValidRfc6455Request_ReturnsSuccessAndAccept()
        {
            string request = BuildValidRequest();

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.Success, result.Status);
            Assert.IsNotNull(result.Request);
            Assert.AreEqual("GET", result.Request!.Method);
            Assert.AreEqual("/", result.Request.RequestTarget);
            Assert.AreEqual("HTTP/1.1", result.Request.HttpVersion);
            Assert.AreEqual("dGhlIHNhbXBsZSBub25jZQ==", result.Request.SecWebSocketKey);
            Assert.AreEqual("13", result.Request.SecWebSocketVersion);
            Assert.AreEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", result.Accept);
        }

        [Test]
        public void ParseRequest_HeaderNamesAreCaseInsensitive()
        {
            string request =
                "GET / HTTP/1.1" + Crlf +
                "host: example.com" + Crlf +
                "UPGRADE: WebSocket" + Crlf +
                "connection: keep-alive, Upgrade" + Crlf +
                "sec-websocket-key: dGhlIHNhbXBsZSBub25jZQ==" + Crlf +
                "Sec-WebSocket-Version: 13" + Crlf +
                Crlf;

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.Success, result.Status);
            Assert.AreEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", result.Accept);
        }

        [Test]
        public void ParseRequest_AcceptsHttp2_0RequestLine()
        {
            string request = BuildValidRequest(version: "HTTP/2.0");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.Success, result.Status);
        }

        // -- ParseRequest: bad request paths --------------------------------

        [Test]
        public void ParseRequest_NonGetMethod_IsBadRequest()
        {
            string request = BuildValidRequest(method: "POST");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("POST", result.FailureReason);
        }

        [Test]
        public void ParseRequest_LowercaseMethod_IsBadRequest()
        {
            // RFC 7230: methods are case-sensitive; "get" must be rejected.
            string request = BuildValidRequest(method: "get");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_OldHttpVersion_IsBadRequest()
        {
            string request = BuildValidRequest(version: "HTTP/1.0");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("HTTP/1.0", result.FailureReason);
        }

        [Test]
        public void ParseRequest_MissingHostHeader_IsBadRequest()
        {
            string request = BuildValidRequest(host: null!);

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("Host", result.FailureReason);
        }

        [Test]
        public void ParseRequest_MissingUpgradeHeader_IsBadRequest()
        {
            string request = BuildValidRequest(upgrade: null!);

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("Upgrade", result.FailureReason);
        }

        [Test]
        public void ParseRequest_UpgradeNotWebSocket_IsBadRequest()
        {
            string request = BuildValidRequest(upgrade: "h2c");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_MissingConnectionHeader_IsBadRequest()
        {
            string request = BuildValidRequest(connection: null!);

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("Connection", result.FailureReason);
        }

        [Test]
        public void ParseRequest_ConnectionWithoutUpgradeToken_IsBadRequest()
        {
            string request = BuildValidRequest(connection: "keep-alive");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_MissingSecWebSocketKey_IsBadRequest()
        {
            string request = BuildValidRequest(key: null!);

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("Sec-WebSocket-Key", result.FailureReason);
        }

        [Test]
        public void ParseRequest_SecWebSocketKeyWrongDecodedSize_IsBadRequest()
        {
            // Base64 of 8 bytes (not 16) — must be rejected.
            string request = BuildValidRequest(key: Convert.ToBase64String(new byte[8]));

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_SecWebSocketKeyNotBase64_IsBadRequest()
        {
            string request = BuildValidRequest(key: "not!base64@@@");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_WrongSecWebSocketVersion_IsBadRequest()
        {
            string request = BuildValidRequest(secVersion: "8");

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
            StringAssert.Contains("Sec-WebSocket-Version", result.FailureReason);
        }

        [Test]
        public void ParseRequest_NoCrlfCrlfTerminator_IsBadRequest()
        {
            string request =
                "GET / HTTP/1.1" + Crlf +
                "Host: localhost" + Crlf +
                "Upgrade: websocket" + Crlf +
                "Connection: Upgrade" + Crlf +
                "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" + Crlf +
                "Sec-WebSocket-Version: 13" + Crlf;
            // missing trailing CRLF

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        [Test]
        public void ParseRequest_MalformedHeaderLine_IsBadRequest()
        {
            string request =
                "GET / HTTP/1.1" + Crlf +
                "Host localhost" + Crlf +
                Crlf;

            var result = HandshakeProcessor.ParseRequest(request);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);
        }

        // -- BuildSuccessResponse / BuildBadRequestResponse -----------------

        [Test]
        public void BuildSuccessResponse_HasRequiredHeaders_AndTerminatesWithCrlfCrlf()
        {
            string response = HandshakeProcessor.BuildSuccessResponse("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");

            StringAssert.StartsWith("HTTP/1.1 101 Switching Protocols" + Crlf, response);
            StringAssert.Contains("Upgrade: websocket" + Crlf, response);
            StringAssert.Contains("Connection: Upgrade" + Crlf, response);
            StringAssert.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=" + Crlf, response);
            StringAssert.EndsWith(Crlf + Crlf, response);
        }

        [Test]
        public void BuildBadRequestResponse_IncludesStatusLineAndBody()
        {
            string response = HandshakeProcessor.BuildBadRequestResponse("Missing Sec-WebSocket-Key");

            StringAssert.StartsWith("HTTP/1.1 400 Bad Request" + Crlf, response);
            StringAssert.Contains("Content-Length: ", response);
            StringAssert.Contains("Connection: close" + Crlf, response);
            StringAssert.EndsWith("Missing Sec-WebSocket-Key", response);
        }

        // -- ProcessAsync end-to-end ----------------------------------------

        [Test]
        public async Task ProcessAsync_ValidRequestOnDuplexStream_WritesSuccessResponse()
        {
            string request = BuildValidRequest();
            using var stream = new DuplexMemoryStream(Encoding.ASCII.GetBytes(request));
            var processor = new HandshakeProcessor();

            var result = await processor.ProcessAsync(stream, CancellationToken.None);

            Assert.AreEqual(HandshakeStatus.Success, result.Status);
            Assert.AreEqual("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", result.Accept);

            string written = Encoding.ASCII.GetString(stream.WrittenBytes);
            StringAssert.StartsWith("HTTP/1.1 101 Switching Protocols", written);
            StringAssert.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", written);
        }

        [Test]
        public async Task ProcessAsync_InvalidRequestOnDuplexStream_WritesBadRequestResponse()
        {
            string request = BuildValidRequest(method: "DELETE");
            using var stream = new DuplexMemoryStream(Encoding.ASCII.GetBytes(request));
            var processor = new HandshakeProcessor();

            var result = await processor.ProcessAsync(stream, CancellationToken.None);

            Assert.AreEqual(HandshakeStatus.BadRequest, result.Status);

            string written = Encoding.UTF8.GetString(stream.WrittenBytes);
            StringAssert.StartsWith("HTTP/1.1 400 Bad Request", written);
        }

        [Test]
        public async Task ProcessAsync_StreamClosedBeforeRequest_ReturnsEndOfStream()
        {
            using var stream = new DuplexMemoryStream(Array.Empty<byte>());
            var processor = new HandshakeProcessor();

            var result = await processor.ProcessAsync(stream, CancellationToken.None);

            Assert.AreEqual(HandshakeStatus.EndOfStream, result.Status);
            Assert.AreEqual(0, stream.WrittenBytes.Length, "Should not write a response when the peer never sent a request.");
        }

        [Test]
        public async Task ProcessAsync_RequestExceedsLimit_ReturnsTooLargeAndWritesBadRequest()
        {
            // Pad headers so the byte count clearly exceeds the cap before CRLFCRLF appears.
            var sb = new StringBuilder();
            sb.Append("GET / HTTP/1.1").Append(Crlf);
            sb.Append("Host: localhost").Append(Crlf);
            sb.Append("X-Filler: ").Append('a', 2048).Append(Crlf);
            // No CRLFCRLF terminator yet — by the time the buffer fills the cap, we should bail.

            using var stream = new DuplexMemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
            var processor = new HandshakeProcessor(maxRequestBytes: 512);

            var result = await processor.ProcessAsync(stream, CancellationToken.None);

            Assert.AreEqual(HandshakeStatus.RequestTooLarge, result.Status);
            string written = Encoding.UTF8.GetString(stream.WrittenBytes);
            StringAssert.StartsWith("HTTP/1.1 400 Bad Request", written);
        }

        // -- DuplexMemoryStream test helper ---------------------------------

        private sealed class DuplexMemoryStream : Stream
        {
            private readonly MemoryStream _read;
            private readonly MemoryStream _write = new();

            public DuplexMemoryStream(byte[] readSource)
            {
                _read = new MemoryStream(readSource ?? Array.Empty<byte>(), writable: false);
            }

            public byte[] WrittenBytes => _write.ToArray();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }
            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public override int Read(byte[] buffer, int offset, int count) => _read.Read(buffer, offset, count);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.FromResult(_read.Read(buffer, offset, count));

            public override void Write(byte[] buffer, int offset, int count) => _write.Write(buffer, offset, count);

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                _write.Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _read.Dispose();
                    _write.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
