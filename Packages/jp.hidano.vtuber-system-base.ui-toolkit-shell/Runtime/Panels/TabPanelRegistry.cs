#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Default <see cref="ITabPanelRegistry"/> implementation. Tracks the
    /// per-tab preload state across the three shell tabs, exposes the
    /// <see cref="ITabLifecycleHandle"/> tab specs use to subscribe to
    /// activation events, and switches the visible tab via
    /// <c>style.display</c> swaps only (Requirement 2.3, 2.4, 2.5, 2.8, 3.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> The registry is not thread-safe. Per design.md
    /// §State Management it is invoked exclusively on the Unity main
    /// thread; the bootstrapper is responsible for marshalling external
    /// callbacks (e.g. UIDocument <c>OnEnable</c>) onto that thread before
    /// reaching the registry. This keeps the implementation a simple
    /// dictionary mutation rather than introducing locks.
    /// </para>
    /// <para>
    /// <b>Failure semantics.</b> A tab can transition <c>Pending → Mounted</c>
    /// or <c>Pending → Failed</c>, but the first non-pending state wins —
    /// late <c>NotifyTabMounted</c> against a failed tab is a no-op so the
    /// failure record persists, and a late <c>MarkTabFailed</c> against a
    /// mounted tab is a no-op so a successful preload is not retroactively
    /// downgraded. Both transitions count toward
    /// <c>IsPreloadComplete</c> per Requirement 3.5: failed tabs do not
    /// block the rest of the shell from booting.
    /// </para>
    /// <para>
    /// <b>Display swap contract.</b> <see cref="SwitchTo"/> only mutates the
    /// <c>style.display</c> of bound <see cref="VisualElement"/> roots — it
    /// never re-clones a VisualTreeAsset, never re-binds a UIDocument, and
    /// never replaces the cached element reference. Tab specs that capture
    /// their root in <c>OnActivated</c> can rely on the same instance
    /// surviving across an arbitrary number of switches (Requirement 2.4,
    /// 3.6). Lifecycle dispatch order on a successful switch is:
    /// (1) hide outgoing root, (2) raise <c>OnDeactivated</c> on outgoing
    /// handle, (3) show incoming root, (4) update <see cref="ActiveTab"/>,
    /// (5) raise <c>OnActivated</c> on incoming handle, (6) raise
    /// <c>OnTabSwitched</c>.
    /// </para>
    /// </remarks>
    public sealed class TabPanelRegistry : ITabPanelRegistry
    {
        private static readonly TabId[] AllTabs =
        {
            TabId.Character,
            TabId.StageLighting,
            TabId.CameraSwitcher,
        };

        private readonly IDiagnosticsLogger _logger;
        private readonly Dictionary<TabId, TabState> _states;
        private readonly Dictionary<TabId, TabLifecycleHandle> _handles;
        private readonly Dictionary<TabId, VisualElement> _roots;
        private TabId? _activeTab;

        public TabPanelRegistry(IDiagnosticsLogger logger)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            _logger = logger;
            _states = new Dictionary<TabId, TabState>(AllTabs.Length);
            _handles = new Dictionary<TabId, TabLifecycleHandle>(AllTabs.Length);
            _roots = new Dictionary<TabId, VisualElement>(AllTabs.Length);
            foreach (var tab in AllTabs)
            {
                _states[tab] = TabState.Pending;
            }
        }

        public int TotalTabCount => AllTabs.Length;

        public bool IsPreloadComplete => CountResolved() == AllTabs.Length;

        public TabId? ActiveTab => _activeTab;

        public event Action<PreloadEvent>? OnPreloadChanged;

        public event Action<TabSwitchEvent>? OnTabSwitched;

        public PreloadProgress GetPreloadProgress()
        {
            var loaded = 0;
            List<TabId>? failed = null;
            foreach (var tab in AllTabs)
            {
                var state = _states[tab];
                if (state == TabState.Mounted)
                {
                    loaded++;
                }
                else if (state == TabState.Failed)
                {
                    loaded++;
                    failed ??= new List<TabId>();
                    failed.Add(tab);
                }
            }
            return new PreloadProgress(
                loaded,
                AllTabs.Length,
                failed);
        }

        public ITabLifecycleHandle RegisterTab(TabId tabId, TabMetadata metadata)
        {
            if (_handles.ContainsKey(tabId))
            {
                throw new InvalidOperationException(
                    $"TabPanelRegistry: TabId.{tabId} is already registered. " +
                    "Dispose the existing ITabLifecycleHandle before re-registering.");
            }
            _ = metadata;
            var handle = new TabLifecycleHandle(this, tabId);
            _handles.Add(tabId, handle);
            return handle;
        }

        public void NotifyTabMounted(TabId tabId)
        {
            if (_states[tabId] != TabState.Pending)
            {
                // Mount-after-mount is the OnEnable re-entrant case.
                // Mount-after-failure preserves the failure (failure wins).
                return;
            }
            _states[tabId] = TabState.Mounted;
            _logger.Log(
                LogLevel.Info,
                LogCategory.Preload,
                $"Tab {tabId} mounted ({CountResolved()}/{AllTabs.Length}).");
            RaisePreloadEvent(tabId, PreloadOutcome.Succeeded);
        }

        public void NotifyTabMounted(TabId tabId, VisualElement rootVisualElement)
        {
            if (rootVisualElement is null)
            {
                throw new ArgumentNullException(nameof(rootVisualElement));
            }
            // Bind the visual element regardless of mount state so that a
            // re-entrant OnEnable can still update the binding without
            // double-firing the preload event.
            _roots[tabId] = rootVisualElement;
            rootVisualElement.style.display = DisplayStyle.None;
            NotifyTabMounted(tabId);
        }

        public void MarkTabFailed(TabId tabId, string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException(
                    "reason must not be null or empty",
                    nameof(reason));
            }
            if (_states[tabId] != TabState.Pending)
            {
                return;
            }
            _states[tabId] = TabState.Failed;
            _logger.Log(
                LogLevel.Error,
                LogCategory.Preload,
                $"Tab {tabId} preload failed: {reason}");
            RaisePreloadEvent(tabId, PreloadOutcome.Failed);
        }

        public SwitchResult SwitchTo(TabId target)
        {
            if (!IsPreloadComplete)
            {
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.TabSwitch,
                    $"SwitchTo({target}) rejected: preload incomplete " +
                    $"({CountResolved()}/{AllTabs.Length}).");
                return SwitchResult.Failed(SwitchErrorCode.PreloadIncomplete);
            }
            if (_states[target] == TabState.Failed)
            {
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.TabSwitch,
                    $"SwitchTo({target}) rejected: tab is in failed state.");
                return SwitchResult.Failed(SwitchErrorCode.TabDisabled);
            }
            if (_activeTab.HasValue && _activeTab.Value == target)
            {
                return SwitchResult.Failed(SwitchErrorCode.AlreadyActive);
            }

            var stopwatch = Stopwatch.StartNew();
            var from = _activeTab;

            // (1) Hide the outgoing tab's root.
            if (from.HasValue && _roots.TryGetValue(from.Value, out var outgoingRoot))
            {
                outgoingRoot.style.display = DisplayStyle.None;
            }

            // (2) Raise OnDeactivated on the outgoing handle (if any) before
            //     the incoming activation so tab specs can save state without
            //     racing against their own OnActivated.
            if (from.HasValue && _handles.TryGetValue(from.Value, out var outgoingHandle))
            {
                outgoingHandle.RaiseDeactivated();
            }

            // (3) Show the incoming tab's root.
            if (_roots.TryGetValue(target, out var incomingRoot))
            {
                incomingRoot.style.display = DisplayStyle.Flex;
            }

            // (4) Update the active-tab pointer before activation events fire
            //     so subscribers reading ActiveTab see the new value.
            _activeTab = target;

            // (5) Raise OnActivated on the incoming handle.
            if (_handles.TryGetValue(target, out var incomingHandle))
            {
                incomingHandle.RaiseActivated();
            }

            stopwatch.Stop();
            var duration = stopwatch.Elapsed;
            var evt = new TabSwitchEvent(from, target, duration);

            _logger.Log(
                LogLevel.Info,
                LogCategory.TabSwitch,
                $"Tab switch {(from.HasValue ? from.Value.ToString() : "<none>")} -> {target} " +
                $"in {duration.TotalMilliseconds:F3}ms.");

            // (6) Publish OnTabSwitched.
            RaiseTabSwitched(evt);

            return SwitchResult.Ok();
        }

        /// <summary>
        /// Backstop sweep — disposes every live <see cref="ITabLifecycleHandle"/>
        /// produced by <see cref="RegisterTab"/>. Used by
        /// <c>UiShellBootstrapper.StopShell</c> so that subscriptions and asset
        /// loader scopes registered through the handle are released even when
        /// the tab spec forgot to call <see cref="IDisposable.Dispose"/>
        /// (Requirement 2.8, 5.7; design.md §Risks). Iteration is performed on
        /// a snapshot copy so the disposing handles can safely mutate the
        /// underlying handle map via <see cref="UnregisterHandle"/>.
        /// </summary>
        public void DisposeAllHandles()
        {
            if (_handles.Count == 0) return;
            var snapshot = new List<TabLifecycleHandle>(_handles.Values);
            foreach (var handle in snapshot)
            {
                try
                {
                    handle.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Log(
                        LogLevel.Warning,
                        LogCategory.Lifecycle,
                        $"TabLifecycleHandle for {handle.TabId} threw during forced dispose: {ex.Message}",
                        ex);
                }
            }
            // Defensive: ensure the map is clear even if a misbehaving handle
            // skipped the UnregisterHandle path.
            _handles.Clear();
        }

        private void RaisePreloadEvent(TabId tabId, PreloadOutcome outcome)
        {
            var handler = OnPreloadChanged;
            if (handler is null) return;
            var evt = new PreloadEvent(tabId, outcome);
            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<PreloadEvent>)subscriber)(evt);
                }
                catch (Exception ex)
                {
                    _logger.Log(
                        LogLevel.Error,
                        LogCategory.Preload,
                        $"OnPreloadChanged subscriber threw for tab {tabId}: {ex.Message}",
                        ex);
                }
            }
        }

        private void RaiseTabSwitched(TabSwitchEvent evt)
        {
            var handler = OnTabSwitched;
            if (handler is null) return;
            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<TabSwitchEvent>)subscriber)(evt);
                }
                catch (Exception ex)
                {
                    _logger.Log(
                        LogLevel.Error,
                        LogCategory.TabSwitch,
                        $"OnTabSwitched subscriber threw for {evt.To}: {ex.Message}",
                        ex);
                }
            }
        }

        private int CountResolved()
        {
            var resolved = 0;
            foreach (var tab in AllTabs)
            {
                if (_states[tab] != TabState.Pending) resolved++;
            }
            return resolved;
        }

        private void UnregisterHandle(TabId tabId)
        {
            _handles.Remove(tabId);
        }

        private enum TabState
        {
            Pending,
            Mounted,
            Failed,
        }

        private sealed class TabLifecycleHandle : ITabLifecycleHandle
        {
            private readonly TabPanelRegistry _owner;
            private readonly List<IDisposable> _trackedResources = new List<IDisposable>();
            private readonly HashSet<IAsyncAssetLoader> _trackedAssetScopes = new HashSet<IAsyncAssetLoader>();
            private bool _disposed;
            private Action? _onActivated;
            private Action? _onDeactivated;

            public TabLifecycleHandle(TabPanelRegistry owner, TabId tabId)
            {
                _owner = owner;
                TabId = tabId;
                ScopeId = $"tab/{tabId}";
            }

            public TabId TabId { get; }

            public bool IsActive { get; private set; }

            public string ScopeId { get; }

            public bool IsDisposed => _disposed;

            public int TrackedResourceCount =>
                _disposed ? 0 : (_trackedResources.Count + _trackedAssetScopes.Count);

            public event Action OnActivated
            {
                add { if (!_disposed) _onActivated += value; }
                remove { _onActivated -= value; }
            }

            public event Action OnDeactivated
            {
                add { if (!_disposed) _onDeactivated += value; }
                remove { _onDeactivated -= value; }
            }

            public void Track(IDisposable resource)
            {
                if (resource is null) throw new ArgumentNullException(nameof(resource));
                if (_disposed)
                {
                    // Late registration after Dispose — drain immediately so
                    // the resource cannot outlive the handle.
                    SafeDispose(resource);
                    return;
                }
                _trackedResources.Add(resource);
            }

            public void TrackAssetScope(IAsyncAssetLoader loader)
            {
                if (loader is null) throw new ArgumentNullException(nameof(loader));
                if (_disposed)
                {
                    try
                    {
                        loader.ReleaseAll(ScopeId);
                    }
                    catch (Exception ex)
                    {
                        _owner._logger.Log(
                            LogLevel.Warning,
                            LogCategory.Lifecycle,
                            $"IAsyncAssetLoader.ReleaseAll({ScopeId}) threw during late TrackAssetScope: {ex.Message}",
                            ex);
                    }
                    return;
                }
                _trackedAssetScopes.Add(loader);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onActivated = null;
                _onDeactivated = null;

                // Dispose tracked resources in reverse registration order so
                // that resources depending on earlier ones unwind first.
                for (var i = _trackedResources.Count - 1; i >= 0; i--)
                {
                    SafeDispose(_trackedResources[i]);
                }
                _trackedResources.Clear();

                foreach (var loader in _trackedAssetScopes)
                {
                    try
                    {
                        loader.ReleaseAll(ScopeId);
                    }
                    catch (Exception ex)
                    {
                        _owner._logger.Log(
                            LogLevel.Warning,
                            LogCategory.Lifecycle,
                            $"IAsyncAssetLoader.ReleaseAll({ScopeId}) threw during handle dispose: {ex.Message}",
                            ex);
                    }
                }
                _trackedAssetScopes.Clear();

                _owner.UnregisterHandle(TabId);
            }

            private void SafeDispose(IDisposable resource)
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception ex)
                {
                    _owner._logger.Log(
                        LogLevel.Warning,
                        LogCategory.Lifecycle,
                        $"Tracked resource disposal threw on TabId.{TabId}: {ex.Message}",
                        ex);
                }
            }

            internal void RaiseActivated()
            {
                if (_disposed) return;
                IsActive = true;
                var handlers = _onActivated;
                if (handlers is null) return;
                foreach (var h in handlers.GetInvocationList())
                {
                    try
                    {
                        ((Action)h)();
                    }
                    catch (Exception ex)
                    {
                        _owner._logger.Log(
                            LogLevel.Error,
                            LogCategory.TabSwitch,
                            $"OnActivated subscriber threw for {TabId}: {ex.Message}",
                            ex);
                    }
                }
            }

            internal void RaiseDeactivated()
            {
                if (_disposed) return;
                IsActive = false;
                var handlers = _onDeactivated;
                if (handlers is null) return;
                foreach (var h in handlers.GetInvocationList())
                {
                    try
                    {
                        ((Action)h)();
                    }
                    catch (Exception ex)
                    {
                        _owner._logger.Log(
                            LogLevel.Error,
                            LogCategory.TabSwitch,
                            $"OnDeactivated subscriber threw for {TabId}: {ex.Message}",
                            ex);
                    }
                }
            }
        }
    }
}
