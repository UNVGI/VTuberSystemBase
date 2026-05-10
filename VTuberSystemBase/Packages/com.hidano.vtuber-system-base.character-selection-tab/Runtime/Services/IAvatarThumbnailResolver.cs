#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Resolves a Sprite for a given avatar key, with deterministic fallback to a
    /// shipped Default sprite. (task 2.4.)
    /// </summary>
    public interface IAvatarThumbnailResolver : IDisposable
    {
        void LoadThumbnail(string avatarKey, string scopeId, Action<AvatarThumbnailResult> onCompleted);
        void Release(string avatarKey, string scopeId);
        void ReleaseAll(string scopeId);
    }

    public readonly struct AvatarThumbnailResult
    {
        public bool Success { get; }
        public Sprite? Sprite { get; }
        public bool IsFallback { get; }
        public LoadError? Error { get; }

        public AvatarThumbnailResult(bool success, Sprite? sprite, bool isFallback, LoadError? error)
        {
            Success = success;
            Sprite = sprite;
            IsFallback = isFallback;
            Error = error;
        }

        public static AvatarThumbnailResult Ok(Sprite sprite, bool isFallback)
            => new AvatarThumbnailResult(true, sprite, isFallback, null);

        public static AvatarThumbnailResult Fail(LoadError error)
            => new AvatarThumbnailResult(false, null, false, error);
    }
}
