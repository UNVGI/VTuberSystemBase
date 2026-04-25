#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Asynchronous result returned by <see cref="IUiCommandClient.RequestAsync{TRequest, TResponse}"/>.
    /// A discriminated-union style struct where <see cref="Success"/> is mutually exclusive with
    /// <see cref="Error"/>; <see cref="Response"/> is populated only on success.
    /// See design.md §Commands §UiCommandClient (Requirement 5.5).
    /// </summary>
    public readonly struct RequestResult<TResponse>
    {
        private RequestResult(bool success, TResponse? response, RequestError? error)
        {
            Success = success;
            Response = response;
            Error = error;
        }

        public bool Success { get; }

        public TResponse? Response { get; }

        public RequestError? Error { get; }

        public static RequestResult<TResponse> Ok(TResponse response) => new RequestResult<TResponse>(true, response, null);

        public static RequestResult<TResponse> Fail(RequestError error) => new RequestResult<TResponse>(false, default, error);
    }

    /// <summary>Failure detail accompanying <see cref="RequestResult{TResponse}.Fail"/>.</summary>
    public readonly struct RequestError : IEquatable<RequestError>
    {
        public RequestError(RequestErrorCode code, string? correlationId = null, string? detail = null)
        {
            Code = code;
            CorrelationId = correlationId;
            Detail = detail;
        }

        public RequestErrorCode Code { get; }

        public string? CorrelationId { get; }

        public string? Detail { get; }

        public bool Equals(RequestError other) =>
            Code == other.Code
            && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
            && string.Equals(Detail, other.Detail, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is RequestError other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Code, CorrelationId, Detail);

        public static bool operator ==(RequestError left, RequestError right) => left.Equals(right);

        public static bool operator !=(RequestError left, RequestError right) => !left.Equals(right);
    }

    /// <summary>
    /// Coarse-grained failure classification for <see cref="IUiCommandClient.RequestAsync{TRequest, TResponse}"/>.
    /// See design.md §Commands §UiCommandClient.
    /// </summary>
    public enum RequestErrorCode
    {
        NotConnected,
        Timeout,
        PayloadTooLarge,
        SerializationFailed,
        TopicInvalid,
        ShellNotRunning,
        Cancelled,
    }
}
