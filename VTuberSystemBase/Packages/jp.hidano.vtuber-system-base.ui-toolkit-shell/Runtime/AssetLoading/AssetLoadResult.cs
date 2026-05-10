#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Discriminated-union-style result for a single <see cref="IAsyncAssetLoader.LoadAsync{T}"/>
    /// completion. <see cref="Success"/> is the canonical discriminator: when true,
    /// <see cref="Asset"/> is non-null and <see cref="Error"/> is null; when false,
    /// <see cref="Error"/> is non-null and <see cref="Asset"/> is null.
    /// </summary>
    public readonly struct AssetLoadResult<T>
        where T : UnityEngine.Object
    {
        private AssetLoadResult(bool success, T? asset, LoadError? error)
        {
            Success = success;
            Asset = asset;
            Error = error;
        }

        public bool Success { get; }

        public T? Asset { get; }

        public LoadError? Error { get; }

        public static AssetLoadResult<T> Ok(T asset)
        {
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            return new AssetLoadResult<T>(success: true, asset: asset, error: null);
        }

        public static AssetLoadResult<T> Fail(LoadError error)
        {
            return new AssetLoadResult<T>(success: false, asset: null, error: error);
        }
    }

    /// <summary>
    /// Failure detail for <see cref="AssetLoadResult{T}"/>. The shell layer converts Addressables
    /// exceptions into <see cref="LoadErrorCode"/> categories so callers do not need to depend on
    /// the underlying loader's exception types.
    /// </summary>
    public readonly struct LoadError
    {
        public LoadError(LoadErrorCode code, string addressableKey, string? detail = null, Exception? innerException = null)
        {
            Code = code;
            AddressableKey = addressableKey ?? string.Empty;
            Detail = detail;
            InnerException = innerException;
        }

        public LoadErrorCode Code { get; }

        public string AddressableKey { get; }

        public string? Detail { get; }

        public Exception? InnerException { get; }
    }

    public enum LoadErrorCode
    {
        KeyNotFound,
        AssetTypeMismatch,
        Cancelled,
        IoFailure,
        AddressablesNotInitialized,
        Unknown,
    }
}
