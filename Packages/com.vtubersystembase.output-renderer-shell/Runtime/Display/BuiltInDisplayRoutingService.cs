#nullable enable
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;

namespace VTuberSystemBase.OutputRendererShell.Display
{
    /// <summary>
    /// <see cref="IDisplayRoutingService"/> の暫定実装。<c>Display.displays[n].Activate()</c> 相当を
    /// <see cref="IDisplayProbe"/> 経由で行い、要求 Display 不在時は Display 0 へフォールバックする。
    /// </summary>
    /// <remarks>
    /// <para>本実装は将来 spec #7 (RuntimeDisplaySelectorIntegration) によって差し替えられる前提（Req 2.5 / 2.6）。</para>
    /// <para>
    /// フォールバック発生時は OR-1 / Req 2.4 に従い <see cref="DisplayAssignmentInfo.IsFallbackActive"/> = true と
    /// 警告ログを残す。Editor PlayMode 上では <see cref="DisplayAssignmentInfo.IsEditorLimitedMode"/> = true を立て、
    /// <see cref="DisplayRoutingConfig.SuppressEditorWarning"/> が false のとき Info ログで通知する。
    /// </para>
    /// </remarks>
    public sealed class BuiltInDisplayRoutingService : IDisplayRoutingService
    {
        private readonly IDisplayProbe _probe;
        private readonly OutputShellLogger _logger;
        private DisplayAssignmentInfo _lastAssignment;
        private bool _disposed;

        /// <summary>
        /// <paramref name="probe"/> を <c>null</c> にすると本番用 <see cref="UnityDisplayProbe"/> が使われる。
        /// テストでは独自スタブを差し替える。
        /// </summary>
        public BuiltInDisplayRoutingService(OutputShellLogger logger, IDisplayProbe? probe = null)
        {
            _logger = logger;
            _probe = probe ?? new UnityDisplayProbe();
        }

        /// <inheritdoc />
        public bool IsFallbackActive => _lastAssignment.IsFallbackActive;

        /// <inheritdoc />
        public DisplayAssignmentInfo GetAssignment() => _lastAssignment;

        /// <inheritdoc />
        public DisplayAssignmentInfo Activate(Camera camera, DisplayRoutingConfig config)
        {
            int requested = config.TargetDisplayIndex;
            int displayCount = _probe.DisplayCount;
            bool isEditor = _probe.IsEditor;

            int effective;
            bool fallback;
            string? diag;

            if (requested >= 0 && requested < displayCount)
            {
                effective = requested;
                fallback = false;
                diag = null;
                _probe.ActivateDisplay(requested);
            }
            else
            {
                effective = 0;
                fallback = true;
                diag = $"Requested Display index {requested} not available (count={displayCount}); falling back to Display 0.";
                _logger.Warning(diag,
                    component: nameof(BuiltInDisplayRoutingService),
                    topic: "display-routing");
            }

            camera.targetDisplay = effective;
            _probe.SetFullScreenMode(config.FullScreenMode);

            if (isEditor && !config.SuppressEditorWarning)
            {
                _logger.Info(
                    "Display.Activate is no-op in Editor PlayMode; physical display routing is honored only in standalone builds.",
                    component: nameof(BuiltInDisplayRoutingService),
                    topic: "editor-limited-mode");
            }

            _lastAssignment = new DisplayAssignmentInfo
            {
                RequestedDisplayIndex = requested,
                EffectiveDisplayIndex = effective,
                IsFallbackActive = fallback,
                IsEditorLimitedMode = isEditor,
                DiagnosticMessage = diag,
            };
            return _lastAssignment;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _disposed = true;
        }
    }
}
