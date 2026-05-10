#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Compact OSC + IPC status indicator + recent failure count
    /// (Requirement 14.5 / 14.9).
    /// </summary>
    public sealed class DiagnosticsBadgeView
    {
        private readonly Func<TabStatus> _statusProvider;
        private readonly Func<bool> _isOscRunning;
        private readonly FailureAggregator _failures;
        private readonly VisualElement _container;
        private readonly VisualElement _ipcIndicator;
        private readonly VisualElement _oscIndicator;
        private readonly Label _failureLabel;

        public DiagnosticsBadgeView(
            Func<TabStatus> statusProvider,
            Func<bool> isOscRunning,
            FailureAggregator failures,
            VisualElement container)
        {
            _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
            _isOscRunning = isOscRunning ?? throw new ArgumentNullException(nameof(isOscRunning));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _container = container ?? throw new ArgumentNullException(nameof(container));

            var badge = new VisualElement();
            badge.AddToClassList("vsb-diagnostics-badge");
            _ipcIndicator = new VisualElement();
            _ipcIndicator.AddToClassList("vsb-diagnostics-badge__indicator");
            badge.Add(_ipcIndicator);
            badge.Add(new Label("IPC"));
            _oscIndicator = new VisualElement();
            _oscIndicator.AddToClassList("vsb-diagnostics-badge__indicator");
            badge.Add(_oscIndicator);
            badge.Add(new Label("OSC"));
            _failureLabel = new Label();
            badge.Add(_failureLabel);
            _container.Clear();
            _container.Add(badge);
        }

        public void Render()
        {
            var status = _statusProvider();
            ApplyClass(_ipcIndicator, status switch
            {
                TabStatus.Ready => "vsb-diagnostics-badge__indicator--ok",
                TabStatus.Suspended => "vsb-diagnostics-badge__indicator--error",
                _ => "vsb-diagnostics-badge__indicator--warn",
            });

            ApplyClass(_oscIndicator, _isOscRunning()
                ? "vsb-diagnostics-badge__indicator--ok"
                : "vsb-diagnostics-badge__indicator--warn");

            _failureLabel.text = _failures.TotalCount > 0
                ? $" {_failures.TotalCount} fail" : "";
        }

        private static void ApplyClass(VisualElement ve, string targetClass)
        {
            ve.RemoveFromClassList("vsb-diagnostics-badge__indicator--ok");
            ve.RemoveFromClassList("vsb-diagnostics-badge__indicator--warn");
            ve.RemoveFromClassList("vsb-diagnostics-badge__indicator--error");
            ve.AddToClassList(targetClass);
        }
    }
}
