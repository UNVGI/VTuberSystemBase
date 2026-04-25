#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.FailsafeAndConnection
{
    /// <summary>
    /// Subscribes to the main-output side <c>output/display/fallback</c> state topic via
    /// <see cref="IUiSubscriptionClient"/> and translates each transition into a corresponding
    /// <see cref="NotificationBarController.ShowDisplayFallback(string, string)"/> /
    /// <see cref="NotificationBarController.ClearDisplayFallback(string)"/> call so the
    /// notification bar mirrors the Display 1 fallback state in real time (Requirement 9.6).
    /// Each transition is also written to the injected <see cref="IDiagnosticsLogger"/> with
    /// <see cref="LogCategory.Connection"/> so the diagnostics surface keeps an audit trail
    /// (Requirement 11.6). See design.md §FailsafeAndConnection §MainOutputStatusWatcher.
    /// </summary>
    /// <remarks>
    /// The watcher does not own the <see cref="NotificationBarController"/>; the bootstrapper
    /// constructs both and disposes them in reverse initialisation order. Disposing the watcher
    /// disposes only its subscription token. Threading mirrors the rest of the shell — the
    /// callback fires on the Unity main thread (D-3 inheritance through
    /// <see cref="UiSubscriptionClient"/>).
    /// </remarks>
    public sealed class MainOutputStatusWatcher : IDisposable
    {
        /// <summary>Topic published by spec #2 (output-renderer-shell) for the Display 1 fallback state.</summary>
        public const string Topic = "output/display/fallback";

        /// <summary>Stable key used when calling <see cref="NotificationBarController.ShowDisplayFallback(string, string)"/>.</summary>
        public const string FallbackKey = "main-output";

        private readonly NotificationBarController _notificationBar;
        private readonly IDiagnosticsLogger _logger;
        private readonly ISubscriptionToken _subscription;
        private bool _disposed;

        public MainOutputStatusWatcher(
            IUiSubscriptionClient subscriptionClient,
            NotificationBarController notificationBar,
            IDiagnosticsLogger logger)
        {
            if (subscriptionClient is null) throw new ArgumentNullException(nameof(subscriptionClient));
            if (notificationBar is null) throw new ArgumentNullException(nameof(notificationBar));
            if (logger is null) throw new ArgumentNullException(nameof(logger));

            _notificationBar = notificationBar;
            _logger = logger;
            _subscription = subscriptionClient.Subscribe<MainOutputStatusPayload>(
                Topic,
                MessageKind.State,
                OnFallbackStateReceived);
        }

        /// <summary>
        /// True while the main output is in the Display 1 fallback condition (the most recent
        /// state observed had <see cref="MainOutputStatusPayload.IsFallback"/> set). False
        /// before any state is received and after a resolution state.
        /// </summary>
        public bool IsInFallback { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription.Dispose();
        }

        private void OnFallbackStateReceived(MessageEnvelope<MainOutputStatusPayload> envelope)
        {
            if (_disposed) return;
            var payload = envelope.Payload;
            if (payload is null)
            {
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.Connection,
                    $"MainOutputStatusWatcher: received null payload on topic={Topic}; ignoring");
                return;
            }

            if (payload.IsFallback)
            {
                IsInFallback = true;
                var message = string.IsNullOrEmpty(payload.Reason)
                    ? "Main output is rendering on Display 1 (fallback). Verify before broadcasting."
                    : $"Main output is rendering on Display 1 (fallback): {payload.Reason}. Verify before broadcasting.";
                _notificationBar.ShowDisplayFallback(FallbackKey, message);
                _logger.Log(
                    LogLevel.Warning,
                    LogCategory.Connection,
                    $"MainOutputStatusWatcher: Display 1 fallback active (reason={payload.Reason ?? "(none)"})");
            }
            else
            {
                IsInFallback = false;
                _notificationBar.ClearDisplayFallback(FallbackKey);
                _logger.Log(
                    LogLevel.Info,
                    LogCategory.Connection,
                    "MainOutputStatusWatcher: Display 1 fallback resolved");
            }
        }
    }
}
