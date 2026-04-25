#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Tab bar controller that owns the three tab buttons rendered by
    /// <c>TabBar.uxml</c>. Wires their <c>Button.clicked</c> events to
    /// <see cref="ITabPanelRegistry.SwitchTo"/>, keeps the buttons in the
    /// disabled visual state until preload completes (Requirement 2.7, 3.2),
    /// activates the initial Character tab when preload completes
    /// (Requirement 3.3), and toggles the
    /// <c>vsb-tab-bar__button--active</c> class to reflect the registry's
    /// <see cref="ITabPanelRegistry.ActiveTab"/> (Requirement 2.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> Like the registry, this controller is single-threaded
    /// and assumes every interaction occurs on the Unity main thread.
    /// </para>
    /// <para>
    /// <b>Failure carve-out.</b> Tabs that the bootstrapper or skin validator
    /// records as failed remain disabled even after preload completes. The
    /// underlying <see cref="ITabPanelRegistry.SwitchTo"/> would already
    /// reject the request with <see cref="SwitchErrorCode.TabDisabled"/>; the
    /// disabled visual is the operator-facing signal (Requirement 3.5, 9.2).
    /// </para>
    /// <para>
    /// <b>No I/O on switch.</b> The click handler dispatches synchronously
    /// into the registry, which only mutates <c>style.display</c>. No
    /// Addressables load, IPC send, or other blocking call is invoked from
    /// this path so the main output surface (Display 2+) can keep meeting
    /// its frame budget (Requirement 2.9).
    /// </para>
    /// </remarks>
    public sealed class TabBarController : IDisposable
    {
        private static readonly TabId[] CanonicalTabOrder =
        {
            TabId.Character,
            TabId.StageLighting,
            TabId.CameraSwitcher,
        };

        private static readonly IReadOnlyDictionary<TabId, string> ButtonNames =
            new Dictionary<TabId, string>
            {
                { TabId.Character, "vsb-tab-bar__button--character" },
                { TabId.StageLighting, "vsb-tab-bar__button--stage-lighting" },
                { TabId.CameraSwitcher, "vsb-tab-bar__button--camera-switcher" },
            };

        /// <summary>USS class applied to every tab button when in the active state (Requirement 2.2).</summary>
        public const string TabBarButtonActiveClass = "vsb-tab-bar__button--active";

        /// <summary>USS class applied to every tab button while preload is in progress or the tab failed (Requirement 2.7, 3.5).</summary>
        public const string TabBarButtonDisabledClass = "vsb-tab-bar__button--disabled";

        private readonly ITabPanelRegistry _registry;
        private readonly IDiagnosticsLogger _logger;
        private readonly Dictionary<TabId, Button> _buttons;
        private readonly Dictionary<TabId, Action> _clickHandlers;
        private readonly TabId _initialTab;

        private bool _disposed;
        private bool _preloadApplied;

        public TabBarController(
            ITabPanelRegistry registry,
            VisualElement tabBarHost,
            IDiagnosticsLogger logger)
            : this(registry, tabBarHost, logger, TabId.Character)
        {
        }

        public TabBarController(
            ITabPanelRegistry registry,
            VisualElement tabBarHost,
            IDiagnosticsLogger logger,
            TabId initialTab)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            if (tabBarHost is null) throw new ArgumentNullException(nameof(tabBarHost));
            if (logger is null) throw new ArgumentNullException(nameof(logger));

            _registry = registry;
            _logger = logger;
            _initialTab = initialTab;

            _buttons = new Dictionary<TabId, Button>(ButtonNames.Count);
            _clickHandlers = new Dictionary<TabId, Action>(ButtonNames.Count);

            foreach (var pair in ButtonNames)
            {
                var button = tabBarHost.Q<Button>(pair.Value);
                if (button is null)
                {
                    throw new InvalidOperationException(
                        $"TabBarController: Button named '{pair.Value}' was not found " +
                        "under the supplied tab bar host. Verify the host root has been " +
                        "cloned from TabBar.uxml (or a skin profile derived from it).");
                }
                _buttons[pair.Key] = button;
                var capturedTabId = pair.Key;
                Action handler = () => HandleTabButtonClicked(capturedTabId);
                _clickHandlers[pair.Key] = handler;
                button.clicked += handler;
                button.AddToClassList(TabBarButtonDisabledClass);
                button.SetEnabled(false);
            }

            _registry.OnPreloadChanged += OnPreloadChanged;
            _registry.OnTabSwitched += OnTabSwitched;

            // The controller may be constructed after the registry has already
            // observed all tab mounts (e.g. late-binding skin reload). Apply
            // the post-preload state synchronously so the initial Character
            // tab still becomes active (Requirement 3.3).
            if (_registry.IsPreloadComplete)
            {
                ApplyPreloadComplete();
            }
        }

        /// <summary>True once preload has completed and the tab bar buttons are interactive (Requirement 3.3).</summary>
        public bool IsEnabled { get; private set; }

        /// <summary>The tab id this controller will activate first when preload completes.</summary>
        public TabId InitialTab => _initialTab;

        /// <summary>
        /// Click entry point shared by the wired <c>Button.clicked</c>
        /// handlers and by tests. Routes the click into
        /// <see cref="ITabPanelRegistry.SwitchTo"/> and logs the outcome under
        /// <see cref="LogCategory.TabSwitch"/> (Requirement 11.2). When
        /// preload has not finished, the click is dropped silently and a
        /// debug-level log is recorded so the operator can correlate the
        /// behaviour against the preload progress (Requirement 2.7).
        /// </summary>
        public void HandleTabButtonClicked(TabId tabId)
        {
            if (_disposed)
            {
                return;
            }
            if (!_registry.IsPreloadComplete)
            {
                _logger.Log(
                    LogLevel.Debug,
                    LogCategory.TabSwitch,
                    $"TabBarController: ignoring click on {tabId}; preload not complete.");
                return;
            }
            var result = _registry.SwitchTo(tabId);
            if (!result.Success)
            {
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.TabSwitch,
                    $"TabBarController: SwitchTo({tabId}) rejected with {result.Error}.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _registry.OnPreloadChanged -= OnPreloadChanged;
            _registry.OnTabSwitched -= OnTabSwitched;

            foreach (var pair in _clickHandlers)
            {
                if (_buttons.TryGetValue(pair.Key, out var button))
                {
                    button.clicked -= pair.Value;
                }
            }
            _clickHandlers.Clear();
        }

        // ---- preload / lifecycle wiring --------------------------------

        private void OnPreloadChanged(PreloadEvent evt)
        {
            if (_disposed) return;
            if (!_registry.IsPreloadComplete) return;
            ApplyPreloadComplete();
        }

        private void OnTabSwitched(TabSwitchEvent evt)
        {
            if (_disposed) return;
            UpdateActiveButtonClasses();
        }

        private void ApplyPreloadComplete()
        {
            if (_preloadApplied) return;
            _preloadApplied = true;
            IsEnabled = true;

            var progress = _registry.GetPreloadProgress();
            var failedTabs = progress.FailedTabs;

            foreach (var pair in _buttons)
            {
                var tabId = pair.Key;
                var button = pair.Value;
                if (IsFailed(failedTabs, tabId))
                {
                    // Failed tabs keep the disabled visual / interactive state
                    // (Requirement 3.5, 9.2) so operators see at a glance which
                    // tab is degraded.
                    button.SetEnabled(false);
                    if (!button.ClassListContains(TabBarButtonDisabledClass))
                    {
                        button.AddToClassList(TabBarButtonDisabledClass);
                    }
                    continue;
                }
                button.RemoveFromClassList(TabBarButtonDisabledClass);
                button.SetEnabled(true);
            }

            // Activate the configured initial tab (Character by default) so the
            // shell starts on a deterministic tab (Requirement 3.3). If that
            // tab failed, fall back to the first non-failed tab in canonical
            // order so the shell never boots with a blank content area.
            var target = ResolveInitialTab(failedTabs);
            if (target.HasValue)
            {
                var result = _registry.SwitchTo(target.Value);
                if (!result.Success)
                {
                    _logger.Log(
                        LogLevel.Warning,
                        LogCategory.TabSwitch,
                        $"TabBarController: initial SwitchTo({target.Value}) rejected with {result.Error}.");
                }
            }
            else
            {
                _logger.Log(
                    LogLevel.Error,
                    LogCategory.TabSwitch,
                    "TabBarController: every tab is in the failed state; no initial tab to activate.");
            }
        }

        private TabId? ResolveInitialTab(IReadOnlyList<TabId> failedTabs)
        {
            if (!IsFailed(failedTabs, _initialTab)) return _initialTab;
            foreach (var tab in CanonicalTabOrder)
            {
                if (!IsFailed(failedTabs, tab)) return tab;
            }
            return null;
        }

        private static bool IsFailed(IReadOnlyList<TabId> failedTabs, TabId tabId)
        {
            if (failedTabs is null) return false;
            for (var i = 0; i < failedTabs.Count; i++)
            {
                if (failedTabs[i] == tabId) return true;
            }
            return false;
        }

        private void UpdateActiveButtonClasses()
        {
            var active = _registry.ActiveTab;
            foreach (var pair in _buttons)
            {
                var match = active.HasValue && active.Value == pair.Key;
                if (match)
                {
                    if (!pair.Value.ClassListContains(TabBarButtonActiveClass))
                    {
                        pair.Value.AddToClassList(TabBarButtonActiveClass);
                    }
                }
                else
                {
                    pair.Value.RemoveFromClassList(TabBarButtonActiveClass);
                }
            }
        }
    }
}
