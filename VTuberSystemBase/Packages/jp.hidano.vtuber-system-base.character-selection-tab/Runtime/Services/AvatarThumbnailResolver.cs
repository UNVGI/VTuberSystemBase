#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Production <see cref="IAvatarThumbnailResolver"/>. Loads
    /// <c>{avatarKey}.thumbnail</c> via <see cref="IAsyncAssetLoader"/> and falls
    /// back to the spec-shipped default sprite (Addressable key configured at
    /// construction) on any failure category. Records a Thumbnail.Fallback log
    /// entry on every fallback. (task 2.4, design.md §AvatarThumbnailResolver.)
    /// </summary>
    public sealed class AvatarThumbnailResolver : IAvatarThumbnailResolver
    {
        private readonly IAsyncAssetLoader _loader;
        private readonly IDiagnosticsLogger? _log;
        private readonly string _defaultKey;
        private bool _disposed;

        public AvatarThumbnailResolver(IAsyncAssetLoader loader, string defaultThumbnailKey, IDiagnosticsLogger? logger = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            if (string.IsNullOrEmpty(defaultThumbnailKey))
                throw new ArgumentException("default key required", nameof(defaultThumbnailKey));
            _defaultKey = defaultThumbnailKey;
            _log = logger;
        }

        public static string ThumbnailKeyFor(string avatarKey) => $"{avatarKey}.thumbnail";

        public void LoadThumbnail(string avatarKey, string scopeId, Action<AvatarThumbnailResult> onCompleted)
        {
            if (string.IsNullOrEmpty(avatarKey)) throw new ArgumentException("avatarKey required", nameof(avatarKey));
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId required", nameof(scopeId));
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));
            ThrowIfDisposed();

            var key = ThumbnailKeyFor(avatarKey);
            _loader.LoadAsync<Sprite>(key, scopeId, result =>
            {
                if (result.Success && result.Asset != null)
                {
                    onCompleted(AvatarThumbnailResult.Ok(result.Asset, isFallback: false));
                    return;
                }
                // Fallback path.
                _log?.Log(LogLevel.Warning, LogCategory.AssetLoad,
                    $"Thumbnail.Fallback: avatarKey={avatarKey} reason={result.Error?.Code}",
                    new { avatarKey, reason = result.Error?.Code.ToString() });
                _loader.LoadAsync<Sprite>(_defaultKey, scopeId, fallback =>
                {
                    if (fallback.Success && fallback.Asset != null)
                    {
                        onCompleted(AvatarThumbnailResult.Ok(fallback.Asset, isFallback: true));
                    }
                    else
                    {
                        onCompleted(AvatarThumbnailResult.Fail(
                            fallback.Error ?? new LoadError(LoadErrorCode.Unknown, _defaultKey, "default missing")));
                    }
                });
            });
        }

        public void Release(string avatarKey, string scopeId)
        {
            ThrowIfDisposed();
            // The shell's loader does not give us the handle from key/scope alone;
            // releasing the whole scope at tab teardown is the documented exit path
            // (TrackAssetScope). Per-key release is intentionally a no-op here so
            // callers cannot accidentally evict an in-flight handle.
        }

        public void ReleaseAll(string scopeId)
        {
            ThrowIfDisposed();
            _loader.ReleaseAll(scopeId);
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AvatarThumbnailResolver));
        }
    }
}
