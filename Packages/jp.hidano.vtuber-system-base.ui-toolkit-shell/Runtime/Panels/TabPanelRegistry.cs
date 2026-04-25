#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Default <see cref="ITabPanelRegistry"/> implementation. Tracks the
    /// per-tab preload state across the three shell tabs and exposes the
    /// <see cref="ITabLifecycleHandle"/> tab specs use to subscribe to
    /// activation events. Display-switching responsibilities (task 8.3) will
    /// extend this class incrementally; the present scope (task 8.2) covers
    /// preload progression, registration, disposal, and the
    /// <c>OnPreloadChanged</c> event surface (Requirements 2.1, 3.1, 3.3,
    /// 3.4, 3.5, 3.6, 3.7, 5.7, 10.1, 10.2).
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

        public TabPanelRegistry(IDiagnosticsLogger logger)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            _logger = logger;
            _states = new Dictionary<TabId, TabState>(AllTabs.Length);
            _handles = new Dictionary<TabId, TabLifecycleHandle>(AllTabs.Length);
            foreach (var tab in AllTabs)
            {
                _states[tab] = TabState.Pending;
            }
        }

        public int TotalTabCount => AllTabs.Length;

        public bool IsPreloadComplete => CountResolved() == AllTabs.Length;

        public event Action<PreloadEvent>? OnPreloadChanged;

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
            private bool _disposed;
            private Action? _onActivated;
            private Action? _onDeactivated;

            public TabLifecycleHandle(TabPanelRegistry owner, TabId tabId)
            {
                _owner = owner;
                TabId = tabId;
            }

            public TabId TabId { get; }

            public bool IsActive { get; private set; }

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

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onActivated = null;
                _onDeactivated = null;
                _owner.UnregisterHandle(TabId);
            }
        }
    }
}
