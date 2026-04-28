#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public enum HandshakeStatus
    {
        Success,
        BadRequest,
        EndOfStream,
        RequestTooLarge,
    }

    public sealed class HandshakeRequest
    {
        public string Method { get; }
        public string RequestTarget { get; }
        public string HttpVersion { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }

        public HandshakeRequest(
            string method,
            string requestTarget,
            string httpVersion,
            IReadOnlyDictionary<string, string> headers)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RequestTarget = requestTarget ?? throw new ArgumentNullException(nameof(requestTarget));
            HttpVersion = httpVersion ?? throw new ArgumentNullException(nameof(httpVersion));
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        }

        public bool TryGetHeader(string name, out string value)
            => Headers.TryGetValue(name, out value!);

        public string? SecWebSocketKey
            => Headers.TryGetValue("Sec-WebSocket-Key", out var v) ? v : null;

        public string? SecWebSocketVersion
            => Headers.TryGetValue("Sec-WebSocket-Version", out var v) ? v : null;
    }

    public readonly struct HandshakeResult
    {
        public HandshakeStatus Status { get; }
        public HandshakeRequest? Request { get; }
        public string? Accept { get; }
        public string? FailureReason { get; }

        private HandshakeResult(
            HandshakeStatus status,
            HandshakeRequest? request,
            string? accept,
            string? failureReason)
        {
            Status = status;
            Request = request;
            Accept = accept;
            FailureReason = failureReason;
        }

        public static HandshakeResult Ok(HandshakeRequest request, string accept)
            => new(HandshakeStatus.Success, request, accept, null);

        public static HandshakeResult Bad(string reason)
            => new(HandshakeStatus.BadRequest, null, null, reason);

        public static HandshakeResult EndOfStream()
            => new(HandshakeStatus.EndOfStream, null, null, "Connection closed before request was received.");

        public static HandshakeResult TooLarge(int observed, int limit)
            => new(
                HandshakeStatus.RequestTooLarge,
                null,
                null,
                $"Handshake request size {observed} bytes exceeds limit {limit} bytes.");
    }

    public sealed class HandshakeProcessor
    {
        public const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        public const int DefaultMaxRequestBytes = 16 * 1024;
        private const string CrLf = "\r\n";
        private const string EndOfHeaders = "\r\n\r\n";

        private readonly int _maxRequestBytes;

        public HandshakeProcessor(int maxRequestBytes = DefaultMaxRequestBytes)
        {
            if (maxRequestBytes < 256)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRequestBytes),
                    "MaxRequestBytes must allow at least 256 bytes for a minimal request.");
            }
            _maxRequestBytes = maxRequestBytes;
        }

        public int MaxRequestBytes => _maxRequestBytes;

        public static string ComputeAccept(string secWebSocketKey)
        {
            if (secWebSocketKey is null) throw new ArgumentNullException(nameof(secWebSocketKey));
            string combined = secWebSocketKey + WebSocketGuid;
            byte[] hashed;
            using (var sha = SHA1.Create())
            {
                hashed = sha.ComputeHash(Encoding.ASCII.GetBytes(combined));
            }
            return Convert.ToBase64String(hashed);
        }

        public static HandshakeResult ParseRequest(string requestText)
        {
            if (requestText is null) throw new ArgumentNullException(nameof(requestText));

            int headerEnd = requestText.IndexOf(EndOfHeaders, StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                return HandshakeResult.Bad("Request did not terminate with CRLF CRLF.");
            }

            string headerBlock = requestText.Substring(0, headerEnd);
            string[] lines = headerBlock.Split(new[] { CrLf }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]))
            {
                return HandshakeResult.Bad("Request line is empty.");
            }

            var requestLine = lines[0];
            string[] requestParts = requestLine.Split(' ');
            if (requestParts.Length != 3)
            {
                return HandshakeResult.Bad("Request line must contain method, target, and HTTP version.");
            }

            string method = requestParts[0];
            string target = requestParts[1];
            string httpVersion = requestParts[2];

            if (!string.Equals(method, "GET", StringComparison.Ordinal))
            {
                return HandshakeResult.Bad($"Method '{method}' is not allowed; only GET is supported for the WebSocket handshake.");
            }

            if (!IsHttp11OrLater(httpVersion))
            {
                return HandshakeResult.Bad($"HTTP version '{httpVersion}' is not supported; require HTTP/1.1 or later.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    return HandshakeResult.Bad($"Malformed header line '{line}'.");
                }
                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (name.Length == 0)
                {
                    return HandshakeResult.Bad("Header line missing name.");
                }
                if (headers.TryGetValue(name, out var existing))
                {
                    headers[name] = existing + ", " + value;
                }
                else
                {
                    headers[name] = value;
                }
            }

            if (!headers.TryGetValue("Host", out _))
            {
                return HandshakeResult.Bad("Required header 'Host' is missing.");
            }

            if (!headers.TryGetValue("Upgrade", out var upgrade)
                || !ContainsToken(upgrade, "websocket"))
            {
                return HandshakeResult.Bad("Required header 'Upgrade: websocket' is missing or invalid.");
            }

            if (!headers.TryGetValue("Connection", out var connection)
                || !ContainsToken(connection, "Upgrade"))
            {
                return HandshakeResult.Bad("Required header 'Connection: Upgrade' is missing or invalid.");
            }

            if (!headers.TryGetValue("Sec-WebSocket-Key", out var key)
                || string.IsNullOrEmpty(key))
            {
                return HandshakeResult.Bad("Required header 'Sec-WebSocket-Key' is missing.");
            }

            if (!IsValidSecWebSocketKey(key))
            {
                return HandshakeResult.Bad("Header 'Sec-WebSocket-Key' must be the Base64 encoding of 16 random bytes.");
            }

            if (!headers.TryGetValue("Sec-WebSocket-Version", out var version)
                || !string.Equals(version, "13", StringComparison.Ordinal))
            {
                return HandshakeResult.Bad("Header 'Sec-WebSocket-Version' must be '13'.");
            }

            var request = new HandshakeRequest(method, target, httpVersion, headers);
            return HandshakeResult.Ok(request, ComputeAccept(key));
        }

        public static string BuildSuccessResponse(string accept)
        {
            if (accept is null) throw new ArgumentNullException(nameof(accept));
            var sb = new StringBuilder(160);
            sb.Append("HTTP/1.1 101 Switching Protocols").Append(CrLf);
            sb.Append("Upgrade: websocket").Append(CrLf);
            sb.Append("Connection: Upgrade").Append(CrLf);
            sb.Append("Sec-WebSocket-Accept: ").Append(accept).Append(CrLf);
            sb.Append(CrLf);
            return sb.ToString();
        }

        public static string BuildBadRequestResponse(string reason)
        {
            string body = string.IsNullOrEmpty(reason) ? "Bad Request" : reason!;
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder(160 + body.Length);
            sb.Append("HTTP/1.1 400 Bad Request").Append(CrLf);
            sb.Append("Content-Type: text/plain; charset=utf-8").Append(CrLf);
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append(CrLf);
            sb.Append("Connection: close").Append(CrLf);
            sb.Append(CrLf);
            sb.Append(body);
            return sb.ToString();
        }

        public async Task<HandshakeResult> ProcessAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            var read = await ReadRequestTextAsync(stream, cancellationToken).ConfigureAwait(false);
            if (read.Status != HandshakeStatus.Success)
            {
                if (read.Status == HandshakeStatus.RequestTooLarge)
                {
                    await WriteBadRequestAsync(stream, read.Failure.FailureReason ?? "Request Too Large", cancellationToken).ConfigureAwait(false);
                }
                return read.Failure;
            }

            var parseResult = ParseRequest(read.RequestText!);
            if (parseResult.Status == HandshakeStatus.Success)
            {
                await WriteSuccessAsync(stream, parseResult.Accept!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteBadRequestAsync(stream, parseResult.FailureReason ?? "Bad Request", cancellationToken).ConfigureAwait(false);
            }
            return parseResult;
        }

        public static async Task WriteSuccessAsync(Stream stream, string accept, CancellationToken cancellationToken)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            byte[] bytes = Encoding.ASCII.GetBytes(BuildSuccessResponse(accept));
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task WriteBadRequestAsync(Stream stream, string reason, CancellationToken cancellationToken)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            byte[] bytes = Encoding.UTF8.GetBytes(BuildBadRequestResponse(reason));
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private readonly struct ReadRequestOutcome
        {
            public HandshakeStatus Status { get; }
            public string? RequestText { get; }
            public HandshakeResult Failure { get; }

            private ReadRequestOutcome(HandshakeStatus status, string? text, HandshakeResult failure)
            {
                Status = status;
                RequestText = text;
                Failure = failure;
            }

            public static ReadRequestOutcome Ok(string text)
                => new(HandshakeStatus.Success, text, default);

            public static ReadRequestOutcome End()
                => new(HandshakeStatus.EndOfStream, null, HandshakeResult.EndOfStream());

            public static ReadRequestOutcome Too(int observed, int limit)
                => new(HandshakeStatus.RequestTooLarge, null, HandshakeResult.TooLarge(observed, limit));
        }

        private async Task<ReadRequestOutcome> ReadRequestTextAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[Math.Min(_maxRequestBytes, 4096)];
            using var ms = new MemoryStream();
            byte[] terminator = { 0x0D, 0x0A, 0x0D, 0x0A };
            int matched = 0;

            while (true)
            {
                int read = await stream
                    .ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    return ReadRequestOutcome.End();
                }

                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i];
                    if (b == terminator[matched])
                    {
                        matched++;
                        if (matched == terminator.Length)
                        {
                            ms.Write(buffer, 0, i + 1);
                            string text = Encoding.ASCII.GetString(ms.ToArray());
                            return ReadRequestOutcome.Ok(text);
                        }
                    }
                    else
                    {
                        matched = b == terminator[0] ? 1 : 0;
                    }
                }

                ms.Write(buffer, 0, read);
                if (ms.Length > _maxRequestBytes)
                {
                    return ReadRequestOutcome.Too((int)ms.Length, _maxRequestBytes);
                }
            }
        }

        private static bool IsHttp11OrLater(string httpVersion)
        {
            const string prefix = "HTTP/";
            if (!httpVersion.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string ver = httpVersion.Substring(prefix.Length);
            int dot = ver.IndexOf('.');
            if (dot <= 0) return false;
            if (!int.TryParse(ver.Substring(0, dot), out int major)) return false;
            if (!int.TryParse(ver.Substring(dot + 1), out int minor)) return false;
            return major > 1 || (major == 1 && minor >= 1);
        }

        private static bool ContainsToken(string headerValue, string token)
        {
            if (string.IsNullOrEmpty(headerValue)) return false;
            string[] parts = headerValue.Split(',');
            foreach (var raw in parts)
            {
                if (string.Equals(raw.Trim(), token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsValidSecWebSocketKey(string key)
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(key);
                return decoded.Length == 16;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
