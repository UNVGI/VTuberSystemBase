#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles
{
    /// <summary>
    /// Independent test double for <see cref="IAsyncAssetLoader"/> used by the
    /// character-selection-tab test suite. Resolves immediately from registered
    /// success / failure tables. Records release calls for scope verification.
    /// </summary>
    public sealed class FakeAsyncAssetLoader : IAsyncAssetLoader
    {
        private readonly Dictionary<string, UnityEngine.Object> _assets =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadError> _failures =
            new Dictionary<string, LoadError>(StringComparer.Ordinal);
        private readonly List<Handle> _live = new List<Handle>();
        public List<string> ScopeReleases { get; } = new List<string>();
        public int LoadCount { get; private set; }

        public void RegisterAsset(string key, UnityEngine.Object asset)
        {
            _failures.Remove(key);
            _assets[key] = asset;
        }

        public void RegisterFailure(string key, LoadErrorCode code, string? detail = null)
        {
            _assets.Remove(key);
            _failures[key] = new LoadError(code, key, detail);
        }

        public IAssetLoadHandle LoadAsync<T>(string addressableKey, string scopeId, Action<AssetLoadResult<T>> onCompleted)
            where T : UnityEngine.Object
        {
            LoadCount++;
            var handle = new Handle { AddressableKey = addressableKey, ScopeId = scopeId };
            _live.Add(handle);
            if (_failures.TryGetValue(addressableKey, out var err))
            {
                handle.State = AssetLoadState.Failed;
                onCompleted(AssetLoadResult<T>.Fail(err));
                return handle;
            }
            if (_assets.TryGetValue(addressableKey, out var obj))
            {
                if (obj is T typed)
                {
                    handle.State = AssetLoadState.Completed;
                    onCompleted(AssetLoadResult<T>.Ok(typed));
                    return handle;
                }
                handle.State = AssetLoadState.Failed;
                onCompleted(AssetLoadResult<T>.Fail(new LoadError(
                    LoadErrorCode.AssetTypeMismatch, addressableKey, "type mismatch")));
                return handle;
            }
            handle.State = AssetLoadState.Failed;
            onCompleted(AssetLoadResult<T>.Fail(new LoadError(
                LoadErrorCode.KeyNotFound, addressableKey, "no registered key")));
            return handle;
        }

        public void Release(IAssetLoadHandle handle)
        {
            if (handle is Handle h)
            {
                h.State = AssetLoadState.Released;
                _live.Remove(h);
            }
        }

        public void ReleaseAll(string scopeId)
        {
            ScopeReleases.Add(scopeId);
            _live.RemoveAll(h => h.ScopeId == scopeId);
        }

        public AssetLoaderSnapshot GetSnapshot()
        {
            return new AssetLoaderSnapshot(0, 0, 0, new Dictionary<string, int>());
        }

        private sealed class Handle : IAssetLoadHandle
        {
            public string AddressableKey { get; init; } = "";
            public string ScopeId { get; init; } = "";
            public AssetLoadState State { get; set; } = AssetLoadState.Pending;
            public void Cancel() { State = AssetLoadState.Cancelled; }
        }
    }
}
