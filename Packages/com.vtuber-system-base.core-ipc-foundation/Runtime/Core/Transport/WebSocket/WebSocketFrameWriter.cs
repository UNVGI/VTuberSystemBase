#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class WebSocketFrameWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _maskOutgoing;
        private readonly Func<uint> _maskingKeyProvider;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        public WebSocketFrameWriter(Stream stream, bool maskOutgoing)
            : this(stream, maskOutgoing, maskingKeyProvider: null)
        {
        }

        public WebSocketFrameWriter(
            Stream stream,
            bool maskOutgoing,
            Func<uint>? maskingKeyProvider)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _maskOutgoing = maskOutgoing;
            _maskingKeyProvider = maskingKeyProvider ?? CreateDefaultMaskingKeyProvider();
        }

        public bool MaskOutgoing => _maskOutgoing;

        public async Task WriteFrameAsync(
            bool fin,
            WebSocketOpcode opcode,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketFrameWriter));
            if (!WebSocketFrameRules.IsKnownOpcode(opcode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(opcode),
                    $"Unknown opcode 0x{(byte)opcode:X}.");
            }

            if (WebSocketFrameRules.IsControlOpcode(opcode))
            {
                if (!fin)
                {
                    throw new ArgumentException(
                        "Control frames must not be fragmented (FIN must be set).",
                        nameof(fin));
                }
                if (payload.Length > WebSocketFrameRules.MaxControlPayloadBytes)
                {
                    throw new ArgumentException(
                        $"Control frame payload must be <= {WebSocketFrameRules.MaxControlPayloadBytes} bytes.",
                        nameof(payload));
                }
            }

            uint? maskingKey = _maskOutgoing ? _maskingKeyProvider() : null;
            byte[] frameBytes = EncodeFrame(fin, opcode, payload.Span, maskingKey);

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(frameBytes, 0, frameBytes.Length, cancellationToken)
                    .ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task WriteTextMessageAsync(string text, CancellationToken cancellationToken)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return WriteFrameAsync(true, WebSocketOpcode.Text, bytes, cancellationToken);
        }

        public Task WritePingAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => WriteFrameAsync(true, WebSocketOpcode.Ping, payload, cancellationToken);

        public Task WritePongAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => WriteFrameAsync(true, WebSocketOpcode.Pong, payload, cancellationToken);

        public Task WriteCloseAsync(
            WebSocketCloseCode code,
            string? reason,
            CancellationToken cancellationToken)
        {
            byte[] reasonBytes = string.IsNullOrEmpty(reason)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(reason!);

            if (2 + reasonBytes.Length > WebSocketFrameRules.MaxControlPayloadBytes)
            {
                throw new ArgumentException(
                    "Close frame payload (status + reason) must be <= 125 bytes.",
                    nameof(reason));
            }

            byte[] payload = new byte[2 + reasonBytes.Length];
            ushort raw = (ushort)code;
            payload[0] = (byte)((raw >> 8) & 0xFF);
            payload[1] = (byte)(raw & 0xFF);
            Buffer.BlockCopy(reasonBytes, 0, payload, 2, reasonBytes.Length);
            return WriteFrameAsync(true, WebSocketOpcode.Close, payload, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writeLock.Dispose();
        }

        public static byte[] EncodeFrame(
            bool fin,
            WebSocketOpcode opcode,
            ReadOnlySpan<byte> payload,
            uint? maskingKey)
        {
            int payloadLength = payload.Length;
            int headerLength = 2;
            if (payloadLength > 125 && payloadLength <= 65535) headerLength += 2;
            else if (payloadLength > 65535) headerLength += 8;
            if (maskingKey.HasValue) headerLength += 4;

            byte[] frame = new byte[headerLength + payloadLength];
            frame[0] = (byte)((fin ? 0x80 : 0x00) | ((byte)opcode & 0x0F));

            byte maskBit = (byte)(maskingKey.HasValue ? 0x80 : 0x00);
            int idx = 2;
            if (payloadLength <= 125)
            {
                frame[1] = (byte)(maskBit | payloadLength);
            }
            else if (payloadLength <= 65535)
            {
                frame[1] = (byte)(maskBit | 126);
                frame[2] = (byte)((payloadLength >> 8) & 0xFF);
                frame[3] = (byte)(payloadLength & 0xFF);
                idx = 4;
            }
            else
            {
                frame[1] = (byte)(maskBit | 127);
                long ll = payloadLength;
                for (int i = 0; i < 8; i++)
                {
                    frame[2 + i] = (byte)((ll >> (56 - i * 8)) & 0xFF);
                }
                idx = 10;
            }

            if (maskingKey.HasValue)
            {
                uint key = maskingKey.Value;
                frame[idx] = (byte)((key >> 24) & 0xFF);
                frame[idx + 1] = (byte)((key >> 16) & 0xFF);
                frame[idx + 2] = (byte)((key >> 8) & 0xFF);
                frame[idx + 3] = (byte)(key & 0xFF);
                int payloadOffset = idx + 4;
                for (int i = 0; i < payloadLength; i++)
                {
                    byte mask = frame[idx + (i & 3)];
                    frame[payloadOffset + i] = (byte)(payload[i] ^ mask);
                }
            }
            else
            {
                payload.CopyTo(frame.AsSpan(idx));
            }

            return frame;
        }

        private static Func<uint> CreateDefaultMaskingKeyProvider()
        {
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            byte[] buffer = new byte[4];
            return () =>
            {
                lock (rng) rng.GetBytes(buffer);
                return ((uint)buffer[0] << 24)
                    | ((uint)buffer[1] << 16)
                    | ((uint)buffer[2] << 8)
                    | buffer[3];
            };
        }
    }
}
