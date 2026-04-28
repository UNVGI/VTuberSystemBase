#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class WebSocketFrameReader
    {
        public const long DefaultMaxMessagePayloadBytes = 1_048_576;

        private readonly Stream _stream;
        private readonly bool _requireMask;
        private readonly long _maxMessagePayloadBytes;
        private readonly byte[] _scratch = new byte[8];

        private WebSocketOpcode? _pendingMessageOpcode;
        private long _pendingMessageBytes;
        private MemoryStream? _pendingMessageBuffer;

        public WebSocketFrameReader(
            Stream stream,
            bool requireMask,
            long maxMessagePayloadBytes = DefaultMaxMessagePayloadBytes)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (maxMessagePayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxMessagePayloadBytes),
                    "MaxMessagePayloadBytes must be non-negative.");
            }
            _requireMask = requireMask;
            _maxMessagePayloadBytes = maxMessagePayloadBytes;
        }

        public bool RequireMask => _requireMask;
        public long MaxMessagePayloadBytes => _maxMessagePayloadBytes;

        public async Task<WebSocketReadResult> ReadFrameAsync(CancellationToken cancellationToken)
        {
            if (!await ReadExactAsync(_scratch, 0, 2, cancellationToken).ConfigureAwait(false))
            {
                return WebSocketReadResult.EndOfStream();
            }

            byte b0 = _scratch[0];
            byte b1 = _scratch[1];

            bool fin = (b0 & 0x80) != 0;
            int rsv = (b0 >> 4) & 0x07;
            if (rsv != 0)
            {
                return WebSocketReadResult.ProtocolError(
                    "RSV1/RSV2/RSV3 bits must be zero (no extensions negotiated).");
            }

            var opcode = (WebSocketOpcode)(b0 & 0x0F);
            if (!WebSocketFrameRules.IsKnownOpcode(opcode))
            {
                return WebSocketReadResult.ProtocolError($"Unknown opcode 0x{(byte)opcode:X}.");
            }

            bool isControl = WebSocketFrameRules.IsControlOpcode(opcode);
            if (isControl && !fin)
            {
                return WebSocketReadResult.ProtocolError("Control frames must not be fragmented.");
            }

            bool masked = (b1 & 0x80) != 0;
            long payloadLength = b1 & 0x7F;

            if (payloadLength == 126)
            {
                if (!await ReadExactAsync(_scratch, 0, 2, cancellationToken).ConfigureAwait(false))
                {
                    return WebSocketReadResult.EndOfStream();
                }
                payloadLength = (_scratch[0] << 8) | _scratch[1];
            }
            else if (payloadLength == 127)
            {
                if (!await ReadExactAsync(_scratch, 0, 8, cancellationToken).ConfigureAwait(false))
                {
                    return WebSocketReadResult.EndOfStream();
                }
                long extended = 0;
                for (int i = 0; i < 8; i++)
                {
                    extended = (extended << 8) | _scratch[i];
                }
                if (extended < 0)
                {
                    return WebSocketReadResult.ProtocolError(
                        "Most significant bit of 64-bit length must be zero.");
                }
                payloadLength = extended;
            }

            if (isControl && payloadLength > WebSocketFrameRules.MaxControlPayloadBytes)
            {
                return WebSocketReadResult.ProtocolError(
                    $"Control frame payload {payloadLength} bytes exceeds max {WebSocketFrameRules.MaxControlPayloadBytes}.");
            }

            if (_requireMask && !masked)
            {
                return WebSocketReadResult.MaskRequired();
            }
            if (!_requireMask && masked)
            {
                return WebSocketReadResult.MaskForbidden();
            }

            byte[]? maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                if (!await ReadExactAsync(maskKey, 0, 4, cancellationToken).ConfigureAwait(false))
                {
                    return WebSocketReadResult.EndOfStream();
                }
            }

            if (payloadLength > _maxMessagePayloadBytes)
            {
                return WebSocketReadResult.MessageTooBig(payloadLength, _maxMessagePayloadBytes);
            }

            byte[] payload;
            if (payloadLength == 0)
            {
                payload = Array.Empty<byte>();
            }
            else
            {
                payload = new byte[payloadLength];
                if (!await ReadExactAsync(payload, 0, (int)payloadLength, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return WebSocketReadResult.EndOfStream();
                }
                if (masked)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] ^= maskKey![i & 3];
                    }
                }
            }

            return WebSocketReadResult.OkFrame(new WebSocketFrame(fin, opcode, masked, payload));
        }

        public async Task<WebSocketReadResult> ReadMessageAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var result = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (result.Status != WebSocketReadStatus.Frame)
                {
                    return result;
                }

                var frame = result.Frame;

                if (frame.Opcode == WebSocketOpcode.Close)
                {
                    var (code, reason) = ParseCloseFrame(frame.Payload);
                    if (frame.Payload.Length == 1)
                    {
                        return WebSocketReadResult.ProtocolError(
                            "Close frame payload of 1 byte is invalid.");
                    }
                    return WebSocketReadResult.Close(code, reason);
                }

                if (frame.IsControl)
                {
                    return WebSocketReadResult.OkFrame(frame);
                }

                if (_pendingMessageOpcode is null)
                {
                    if (frame.Opcode == WebSocketOpcode.Continuation)
                    {
                        return WebSocketReadResult.ProtocolError(
                            "Continuation frame received without an initial data frame.");
                    }
                    _pendingMessageOpcode = frame.Opcode;
                    _pendingMessageBytes = 0;
                    _pendingMessageBuffer = null;
                }
                else
                {
                    if (frame.Opcode != WebSocketOpcode.Continuation)
                    {
                        return WebSocketReadResult.ProtocolError(
                            "Expected a continuation frame; got a new data frame.");
                    }
                }

                long newTotal = _pendingMessageBytes + frame.Payload.Length;
                if (newTotal > _maxMessagePayloadBytes)
                {
                    ResetPendingMessage();
                    return WebSocketReadResult.MessageTooBig(newTotal, _maxMessagePayloadBytes);
                }

                if (frame.Fin)
                {
                    byte[] full;
                    if (_pendingMessageBuffer is null)
                    {
                        full = frame.Payload;
                    }
                    else
                    {
                        _pendingMessageBuffer.Write(frame.Payload, 0, frame.Payload.Length);
                        full = _pendingMessageBuffer.ToArray();
                    }

                    var assembledOpcode = _pendingMessageOpcode!.Value;
                    bool wasMasked = frame.WasMasked;
                    ResetPendingMessage();

                    if (assembledOpcode == WebSocketOpcode.Text && !IsValidUtf8(full))
                    {
                        return WebSocketReadResult.InvalidUtf8();
                    }

                    return WebSocketReadResult.OkFrame(
                        new WebSocketFrame(true, assembledOpcode, wasMasked, full));
                }
                else
                {
                    _pendingMessageBuffer ??= new MemoryStream();
                    _pendingMessageBuffer.Write(frame.Payload, 0, frame.Payload.Length);
                    _pendingMessageBytes = newTotal;
                }
            }
        }

        private void ResetPendingMessage()
        {
            _pendingMessageOpcode = null;
            _pendingMessageBytes = 0;
            _pendingMessageBuffer = null;
        }

        private async Task<bool> ReadExactAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < count)
            {
                int read = await _stream
                    .ReadAsync(buffer, offset + total, count - total, cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        public static (WebSocketCloseCode? Code, string? Reason) ParseCloseFrame(byte[] payload)
        {
            if (payload is null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length == 0) return (null, null);
            if (payload.Length == 1) return (null, null);

            ushort raw = (ushort)((payload[0] << 8) | payload[1]);
            var code = (WebSocketCloseCode)raw;
            string? reason = null;
            if (payload.Length > 2)
            {
                try
                {
                    var utf8 = new UTF8Encoding(false, true);
                    reason = utf8.GetString(payload, 2, payload.Length - 2);
                }
                catch (DecoderFallbackException)
                {
                    reason = null;
                }
            }
            return (code, reason);
        }

        public static bool IsValidUtf8(byte[] bytes)
        {
            if (bytes is null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0) return true;
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                _ = utf8.GetCharCount(bytes, 0, bytes.Length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
