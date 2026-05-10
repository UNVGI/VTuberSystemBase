#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Synchronous result returned by <see cref="IUiCommandClient.PublishState{TPayload}"/> and
    /// <see cref="IUiCommandClient.PublishEvent{TPayload}"/>. A discriminated-union style struct
    /// where <see cref="Success"/> is mutually exclusive with <see cref="Error"/>.
    /// See design.md §Commands §UiCommandClient (Requirements 5.2, 5.3, 5.4, 5.9).
    /// </summary>
    public readonly struct SendResult : IEquatable<SendResult>
    {
        private SendResult(bool success, SendError? error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }

        public SendError? Error { get; }

        public static SendResult Ok() => new SendResult(true, null);

        public static SendResult Fail(SendError error) => new SendResult(false, error);

        public bool Equals(SendResult other) => Success == other.Success && Nullable.Equals(Error, other.Error);

        public override bool Equals(object? obj) => obj is SendResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Success, Error);

        public static bool operator ==(SendResult left, SendResult right) => left.Equals(right);

        public static bool operator !=(SendResult left, SendResult right) => !left.Equals(right);
    }

    /// <summary>Failure detail accompanying <see cref="SendResult.Fail"/>.</summary>
    public readonly struct SendError : IEquatable<SendError>
    {
        public SendError(SendErrorCode code, string? detail = null)
        {
            Code = code;
            Detail = detail;
        }

        public SendErrorCode Code { get; }

        public string? Detail { get; }

        public bool Equals(SendError other) => Code == other.Code && string.Equals(Detail, other.Detail, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SendError other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Code, Detail);

        public static bool operator ==(SendError left, SendError right) => left.Equals(right);

        public static bool operator !=(SendError left, SendError right) => !left.Equals(right);
    }

    /// <summary>
    /// Coarse-grained failure classification for <see cref="IUiCommandClient.PublishState{TPayload}"/> /
    /// <see cref="IUiCommandClient.PublishEvent{TPayload}"/>. See design.md §Commands §UiCommandClient.
    /// </summary>
    public enum SendErrorCode
    {
        NotConnected,
        PayloadTooLarge,
        SerializationFailed,
        TopicInvalid,
        ShellNotRunning,
    }
}
