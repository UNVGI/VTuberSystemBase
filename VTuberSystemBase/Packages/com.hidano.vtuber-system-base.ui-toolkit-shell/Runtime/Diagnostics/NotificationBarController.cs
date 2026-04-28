#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Drives the notification bar (the <c>vsb-notification-bar</c> region of
    /// the root UIDocument) by translating <see cref="IConnectionStatus"/>
    /// transitions, <see cref="ITabPanelRegistry.OnPreloadChanged"/> failures,
    /// and externally pushed Display 1 fallback warnings into a stacked list of
    /// dismissible notification rows (Requirements 6.6, 9.5, 9.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stacking and overflow.</b> The notification bar shows at most
    /// <see cref="MaxStackedNotifications"/> items vertically; additional
    /// active notifications are recorded through the supplied
    /// <see cref="IDiagnosticsLogger"/> with <see cref="LogLevel.Warning"/> so
    /// the diagnostics panel can pick them up (design.md §Diagnostics
    /// §NotificationBarController, Implementation Notes / Risks).
    /// </para>
    /// <para>
    /// <b>Dismissal semantics.</b> The per-row close button removes the row
    /// entirely. If the underlying state continues and another transition
    /// arrives (e.g. the connection drops again, or another preload event
    /// fires for the same tab), the warning re-emerges.
    /// </para>
    /// <para>
    /// <b>Threading.</b> The controller assumes every interaction occurs on
    /// the Unity main thread, mirroring the rest of the shell (design.md
    /// §State Management).
    /// </para>
    /// </remarks>
    public sealed class NotificationBarController : IDisposable
    {
        public const string NotificationItemClass = "vsb-notification-bar__item";
        public const string ItemConnectionClass = "vsb-notification-bar__item--connection";
        public const string ItemDisplayFallbackClass = "vsb-notification-bar__item--display-fallback";
        public const string ItemPreloadFailureClass = "vsb-notification-bar__item--preload-failure";
        public const string ItemMessageClass = "vsb-notification-bar__item-message";
        public const string ItemCloseClass = "vsb-notification-bar__item-close";

        private const string ConnectionKey = "connection";
        private const string DisplayFallbackKeyPrefix = "display-fallback:";
        private const string PreloadFailureKeyPrefix = "preload-failure:";

        public const int MaxStackedNotifications = 3;

        private readonly VisualElement _host;
        private readonly IConnectionStatus _connectionStatus;
        private readonly ITabPanelRegistry _registry;
        private readonly IDiagnosticsLogger _logger;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Action<ConnectionStatusEvent> _connectionHandler;
        private readonly Action<PreloadEvent> _preloadHandler;

        private bool _disposed;

        public NotificationBarController(
            VisualElement notificationBarHost,
            IConnectionStatus connectionStatus,
            ITabPanelRegistry registry,
            IDiagnosticsLogger logger)
        {
            if (notificationBarHost is null) throw new ArgumentNullException(nameof(notificationBarHost));
            if (connectionStatus is null) throw new ArgumentNullException(nameof(connectionStatus));
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            if (logger is null) throw new ArgumentNullException(nameof(logger));

            _host = notificationBarHost;
            _connectionStatus = connectionStatus;
            _registry = registry;
            _logger = logger;

            _connectionHandler = OnConnectionStatusChanged;
            _preloadHandler = OnPreloadChanged;

            _connectionStatus.OnStatusChanged += _connectionHandler;
            _registry.OnPreloadChanged += _preloadHandler;
        }

        /// <summary>Total number of active (undismissed) notifications, including those overflowed past the visual cap.</summary>
        public int ActiveNotificationCount => _entries.Count;

        /// <summary>
        /// Pushes a Display 1 fallback warning surfaced by
        /// <c>MainOutputStatusWatcher</c> (task 9.2). Same <paramref name="key"/>
        /// replaces the previous message instead of stacking a duplicate row.
        /// </summary>
        public void ShowDisplayFallback(string key, string message)
        {
            if (_disposed) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key must be non-empty", nameof(key));
            if (message is null) throw new ArgumentNullException(nameof(message));

            AddOrReplace(DisplayFallbackKeyPrefix + key, NotificationCategory.DisplayFallback, message);
        }

        /// <summary>Removes a previously published Display 1 fallback warning by its key.</summary>
        public void ClearDisplayFallback(string key)
        {
            if (_disposed) return;
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key must be non-empty", nameof(key));

            RemoveByKey(DisplayFallbackKeyPrefix + key);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _connectionStatus.OnStatusChanged -= _connectionHandler;
            _registry.OnPreloadChanged -= _preloadHandler;

            foreach (var entry in _entries)
            {
                if (entry.Element is not null && entry.Element.parent == _host)
                {
                    _host.Remove(entry.Element);
                }
            }
            _entries.Clear();
        }

        // ---- event handlers --------------------------------------------

        private void OnConnectionStatusChanged(ConnectionStatusEvent evt)
        {
            if (_disposed) return;
            switch (evt.To)
            {
                case ConnectionStatusCode.Connected:
                case ConnectionStatusCode.Initializing:
                case ConnectionStatusCode.Connecting:
                    RemoveByKey(ConnectionKey);
                    break;
                case ConnectionStatusCode.Disconnected:
                case ConnectionStatusCode.Reconnecting:
                case ConnectionStatusCode.FailedPermanently:
                    AddOrReplace(ConnectionKey, NotificationCategory.Connection, FormatConnectionMessage(evt.To));
                    break;
            }
        }

        private void OnPreloadChanged(PreloadEvent evt)
        {
            if (_disposed) return;
            if (evt.Outcome != PreloadOutcome.Failed) return;

            AddOrReplace(
                PreloadFailureKeyPrefix + evt.TabId,
                NotificationCategory.PreloadFailure,
                $"Preload failed: {evt.TabId}");
        }

        // ---- state mutation --------------------------------------------

        private void AddOrReplace(string key, NotificationCategory category, string message)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                {
                    _entries[i].Message = message;
                    Rerender();
                    return;
                }
            }

            _entries.Add(new Entry(key, category, message));
            Rerender();
        }

        private void RemoveByKey(string key)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                {
                    var entry = _entries[i];
                    DetachElement(entry);
                    _entries.RemoveAt(i);
                    Rerender();
                    return;
                }
            }
        }

        private static void DetachElement(Entry entry)
        {
            if (entry.Element is { parent: { } parent })
            {
                parent.Remove(entry.Element);
            }
            entry.Element = null;
        }

        private void DismissByKey(string key)
        {
            // Operator-driven dismissal: drop the entry entirely. If the
            // underlying state persists, a subsequent event will reinsert it.
            RemoveByKey(key);
        }

        // ---- rendering -------------------------------------------------

        private void Rerender()
        {
            // Detach existing elements so we can re-attach them in the canonical order.
            foreach (var entry in _entries)
            {
                if (entry.Element is not null && entry.Element.parent == _host)
                {
                    _host.Remove(entry.Element);
                }
            }

            var visibleCount = Math.Min(_entries.Count, MaxStackedNotifications);
            for (var i = 0; i < visibleCount; i++)
            {
                var entry = _entries[i];
                if (entry.Element is null)
                {
                    entry.Element = BuildItemElement(entry);
                }
                else
                {
                    UpdateMessage(entry.Element, entry.Message);
                }
                _host.Add(entry.Element);
            }

            if (_entries.Count > MaxStackedNotifications)
            {
                var overflow = _entries.Count - MaxStackedNotifications;
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.Lifecycle,
                    $"NotificationBarController: {overflow} notification(s) overflow the {MaxStackedNotifications}-item visual cap and are kept only in the diagnostics panel.");
            }
        }

        private VisualElement BuildItemElement(Entry entry)
        {
            var item = new VisualElement { name = entry.Key };
            item.AddToClassList(NotificationItemClass);
            item.AddToClassList(CategoryClass(entry.Category));

            var message = new Label(entry.Message) { name = entry.Key + "__message" };
            message.AddToClassList(ItemMessageClass);
            item.Add(message);

            var close = new Button(() => DismissByKey(entry.Key))
            {
                name = entry.Key + "__close",
                text = "x",
            };
            close.AddToClassList(ItemCloseClass);
            item.Add(close);

            return item;
        }

        private static void UpdateMessage(VisualElement item, string message)
        {
            var label = item.Q<Label>(className: ItemMessageClass);
            if (label is not null)
            {
                label.text = message;
            }
        }

        private static string CategoryClass(NotificationCategory category)
        {
            switch (category)
            {
                case NotificationCategory.Connection:
                    return ItemConnectionClass;
                case NotificationCategory.DisplayFallback:
                    return ItemDisplayFallbackClass;
                case NotificationCategory.PreloadFailure:
                    return ItemPreloadFailureClass;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown category");
            }
        }

        private static string FormatConnectionMessage(ConnectionStatusCode code)
        {
            switch (code)
            {
                case ConnectionStatusCode.Disconnected:
                    return "IPC connection lost.";
                case ConnectionStatusCode.Reconnecting:
                    return "IPC connection: reconnecting...";
                case ConnectionStatusCode.FailedPermanently:
                    return "IPC connection failed permanently.";
                default:
                    return $"IPC connection state: {code}.";
            }
        }

        // ---- types -----------------------------------------------------

        private enum NotificationCategory
        {
            Connection,
            DisplayFallback,
            PreloadFailure,
        }

        private sealed class Entry
        {
            public Entry(string key, NotificationCategory category, string message)
            {
                Key = key;
                Category = category;
                Message = message;
            }

            public string Key { get; }
            public NotificationCategory Category { get; }
            public string Message { get; set; }
            public VisualElement? Element { get; set; }
        }
    }
}
