#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Facade abstraction for the ui-toolkit-shell's asynchronous asset loading subsystem.
    /// Production implementations wrap Unity Addressables; test implementations
    /// (<c>FakeAsyncAssetLoader</c>) allow controlling completion timing, failure injection,
    /// cancellation, and snapshot output without touching the real Addressables runtime.
    /// </summary>
    /// <remarks>
    /// Contract highlights (see design.md §AssetLoading):
    /// - <c>LoadAsync</c> returns a handle synchronously and never blocks the main thread.
    /// - Completion is delivered exclusively via <c>onCompleted</c>; the same handle's callback
    ///   is invoked at most once over its lifetime, on the Unity main thread.
    /// - <c>Release</c> / <c>ReleaseAll</c> form the symmetric unload API; releasing an
    ///   in-flight handle causes its callback to fire with <see cref="LoadErrorCode.Cancelled"/>.
    /// - <c>GetSnapshot</c> is read-only and side-effect free; it is safe to call from
    ///   diagnostics paths.
    /// </remarks>
    public interface IAsyncAssetLoader
    {
        IAssetLoadHandle LoadAsync<T>(
            string addressableKey,
            string scopeId,
            Action<AssetLoadResult<T>> onCompleted)
            where T : UnityEngine.Object;

        void Release(IAssetLoadHandle handle);

        void ReleaseAll(string scopeId);

        AssetLoaderSnapshot GetSnapshot();
    }
}
