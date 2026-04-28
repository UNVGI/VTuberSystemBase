#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public enum WebSocketOpcode : byte
    {
        Continuation = 0x0,
        Text = 0x1,
        Binary = 0x2,
        Close = 0x8,
        Ping = 0x9,
        Pong = 0xA,
    }

    public enum WebSocketCloseCode : ushort
    {
        NormalClosure = 1000,
        GoingAway = 1001,
        ProtocolError = 1002,
        UnsupportedData = 1003,
        InvalidFramePayloadData = 1007,
        PolicyViolation = 1008,
        MessageTooBig = 1009,
        MandatoryExtension = 1010,
        InternalServerError = 1011,
    }

    public enum WebSocketReadStatus
    {
        Frame,
        Close,
        EndOfStream,
        ProtocolError,
        MessageTooBig,
        InvalidUtf8,
        MaskRequired,
        MaskForbidden,
    }

    public readonly struct WebSocketFrame
    {
        public bool Fin { get; }
        public WebSocketOpcode Opcode { get; }
        public bool WasMasked { get; }
        public byte[] Payload { get; }

        public WebSocketFrame(bool fin, WebSocketOpcode opcode, bool wasMasked, byte[] payload)
        {
            Fin = fin;
            Opcode = opcode;
            WasMasked = wasMasked;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public bool IsControl => WebSocketFrameRules.IsControlOpcode(Opcode);
        public bool IsData => Opcode == WebSocketOpcode.Text
            || Opcode == WebSocketOpcode.Binary
            || Opcode == WebSocketOpcode.Continuation;
    }

    public readonly struct WebSocketReadResult
    {
        public WebSocketReadStatus Status { get; }
        public WebSocketFrame Frame { get; }
        public WebSocketCloseCode? CloseCode { get; }
        public string? CloseReason { get; }
        public string? ErrorMessage { get; }
        public long ObservedSize { get; }
        public long LimitSize { get; }

        private WebSocketReadResult(
            WebSocketReadStatus status,
            WebSocketFrame frame,
            WebSocketCloseCode? closeCode,
            string? closeReason,
            string? errorMessage,
            long observedSize,
            long limitSize)
        {
            Status = status;
            Frame = frame;
            CloseCode = closeCode;
            CloseReason = closeReason;
            ErrorMessage = errorMessage;
            ObservedSize = observedSize;
            LimitSize = limitSize;
        }

        public static WebSocketReadResult OkFrame(WebSocketFrame frame)
            => new(WebSocketReadStatus.Frame, frame, null, null, null, 0, 0);

        public static WebSocketReadResult Close(WebSocketCloseCode? code, string? reason)
            => new(WebSocketReadStatus.Close, default, code, reason, null, 0, 0);

        public static WebSocketReadResult EndOfStream()
            => new(WebSocketReadStatus.EndOfStream, default, null, null, null, 0, 0);

        public static WebSocketReadResult ProtocolError(string message)
            => new(WebSocketReadStatus.ProtocolError, default, null, null, message, 0, 0);

        public static WebSocketReadResult MessageTooBig(long observed, long limit)
            => new(
                WebSocketReadStatus.MessageTooBig,
                default,
                null,
                null,
                $"WebSocket payload size {observed} bytes exceeds limit {limit} bytes.",
                observed,
                limit);

        public static WebSocketReadResult InvalidUtf8()
            => new(
                WebSocketReadStatus.InvalidUtf8,
                default,
                null,
                null,
                "Text frame payload is not a valid UTF-8 sequence.",
                0,
                0);

        public static WebSocketReadResult MaskRequired()
            => new(
                WebSocketReadStatus.MaskRequired,
                default,
                null,
                null,
                "Frames sent from client to server must be masked.",
                0,
                0);

        public static WebSocketReadResult MaskForbidden()
            => new(
                WebSocketReadStatus.MaskForbidden,
                default,
                null,
                null,
                "Frames sent from server to client must not be masked.",
                0,
                0);
    }

    internal static class WebSocketFrameRules
    {
        public const int MaxControlPayloadBytes = 125;

        public static bool IsControlOpcode(WebSocketOpcode opcode)
            => ((byte)opcode & 0x08) != 0;

        public static bool IsKnownOpcode(WebSocketOpcode opcode)
        {
            switch (opcode)
            {
                case WebSocketOpcode.Continuation:
                case WebSocketOpcode.Text:
                case WebSocketOpcode.Binary:
                case WebSocketOpcode.Close:
                case WebSocketOpcode.Ping:
                case WebSocketOpcode.Pong:
                    return true;
                default:
                    return false;
            }
        }
    }
}
