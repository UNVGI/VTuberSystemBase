#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="ITabLifecycleHandle"/>. Tracks Track()'d
    /// resources and asset-scope registrations so disposal verification is
    /// straightforward; <see cref="FireActivated"/> / <see cref="FireDeactivated"/>
    /// drive the lifecycle events.
    /// </summary>
    public sealed class FakeTabLifecycleHandle : ITabLifecycleHandle
    {
        private readonly List<IDisposable> _tracked = new List<IDisposable>();
        private readonly List<IAsyncAssetLoader> _scopes = new List<IAsyncAssetLoader>();

        public FakeTabLifecycleHandle(TabId tabId = TabId.Character, string scopeId = "tab:character")
        {
            TabId = tabId;
            ScopeId = scopeId;
        }

        public TabId TabId { get; }
        public bool IsActive { get; private set; }
        public string ScopeId { get; }
        public bool IsDisposed { get; private set; }
        public int TrackedResourceCount => _tracked.Count;

        public event Action? OnActivated;
        public event Action? OnDeactivated;

        public void FireActivated() { IsActive = true; OnActivated?.Invoke(); }
        public void FireDeactivated() { IsActive = false; OnDeactivated?.Invoke(); }

        public void Track(IDisposable resource)
        {
            if (IsDisposed) { resource.Dispose(); return; }
            _tracked.Add(resource);
        }

        public void TrackAssetScope(IAsyncAssetLoader loader)
        {
            if (_scopes.Contains(loader)) return;
            _scopes.Add(loader);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var d in _tracked)
            {
                try { d.Dispose(); } catch { /* swallow per Track() contract */ }
            }
            _tracked.Clear();
            foreach (var loader in _scopes)
            {
                try { loader.ReleaseAll(ScopeId); } catch { /* idempotent */ }
            }
            _scopes.Clear();
        }
    }
}
