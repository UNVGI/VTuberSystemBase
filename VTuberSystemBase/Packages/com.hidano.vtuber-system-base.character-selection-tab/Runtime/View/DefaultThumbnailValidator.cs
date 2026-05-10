#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.View
{
    /// <summary>
    /// Bootstrap-time guard that probes the configured default-thumbnail Addressable
    /// key once on startup and emits a diagnostic error when it cannot be resolved.
    /// (task 4.3, design.md §AvatarThumbnailResolver Risks.)
    /// <para>
    /// The default thumbnail is consumed by <c>AvatarThumbnailResolver</c> as the
    /// fallback when an avatar's <c>{avatarKey}.thumbnail</c> entry is missing or
    /// type-mismatched. If the default itself is unreachable, every fallback
    /// branch silently degrades; surfacing the failure here lets integrators see
    /// the misconfiguration before users hit it.
    /// </para>
    /// </summary>
    public static class DefaultThumbnailValidator
    {
        /// <summary>
        /// Issues a one-shot probe load of <paramref name="defaultKey"/>.
        /// On failure, logs at <see cref="LogLevel.Error"/>.
        /// On success, releases the probe handle immediately so it does not pin
        /// memory beyond the validation window.
        /// </summary>
        public static void ValidateAsync(
            IAsyncAssetLoader loader,
            string defaultKey,
            string scopeId,
            IDiagnosticsLogger? logger,
            Action<bool>? onCompleted = null)
        {
            if (loader is null) throw new ArgumentNullException(nameof(loader));
            if (string.IsNullOrEmpty(defaultKey)) throw new ArgumentException("defaultKey required", nameof(defaultKey));
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId required", nameof(scopeId));

            var probeScope = scopeId + ":default-thumbnail-probe";
            loader.LoadAsync<Sprite>(defaultKey, probeScope, result =>
            {
                if (result.Success && result.Asset != null)
                {
                    logger?.Log(LogLevel.Debug, LogCategory.AssetLoad,
                        $"DefaultThumbnail.Probe ok key={defaultKey}");
                    loader.ReleaseAll(probeScope);
                    onCompleted?.Invoke(true);
                    return;
                }
                logger?.Log(LogLevel.Error, LogCategory.AssetLoad,
                    $"DefaultThumbnail.Probe failed key={defaultKey} reason={result.Error?.Code}",
                    new { defaultKey, reason = result.Error?.Code.ToString() });
                onCompleted?.Invoke(false);
            });
        }
    }
}
