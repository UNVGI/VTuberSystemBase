#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public readonly struct IpcResult : IEquatable<IpcResult>
    {
        public bool Success { get; }
        public CoreIpcError? Error { get; }

        public IpcResult(bool success, CoreIpcError? error)
        {
            Success = success;
            Error = error;
        }

        public static IpcResult Ok() => new(true, null);

        public static IpcResult Fail(CoreIpcError error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            return new IpcResult(false, error);
        }

        public bool Equals(IpcResult other) => Success == other.Success && Equals(Error, other.Error);

        public override bool Equals(object? obj) => obj is IpcResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Success, Error);

        public static bool operator ==(IpcResult left, IpcResult right) => left.Equals(right);

        public static bool operator !=(IpcResult left, IpcResult right) => !left.Equals(right);
    }

    public readonly struct IpcResult<T> : IEquatable<IpcResult<T>>
    {
        public bool Success { get; }
        public T? Value { get; }
        public CoreIpcError? Error { get; }

        public IpcResult(bool success, T? value, CoreIpcError? error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static IpcResult<T> Ok(T value) => new(true, value, null);

        public static IpcResult<T> Fail(CoreIpcError error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            return new IpcResult<T>(false, default, error);
        }

        public bool Equals(IpcResult<T> other) =>
            Success == other.Success
            && Equals(Value, other.Value)
            && Equals(Error, other.Error);

        public override bool Equals(object? obj) => obj is IpcResult<T> other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Success, Value, Error);

        public static bool operator ==(IpcResult<T> left, IpcResult<T> right) => left.Equals(right);

        public static bool operator !=(IpcResult<T> left, IpcResult<T> right) => !left.Equals(right);
    }
}
