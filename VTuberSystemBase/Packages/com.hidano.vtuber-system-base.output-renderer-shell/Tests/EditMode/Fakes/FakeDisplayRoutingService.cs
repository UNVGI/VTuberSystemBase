#nullable enable
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests.Fakes
{
    /// <summary>
    /// <see cref="IDisplayRoutingService"/> のテスト用フェイク実装。
    /// </summary>
    /// <remarks>
    /// <see cref="Activate(Camera, DisplayRoutingConfig)"/> 呼び出し履歴を保持し、
    /// 任意の <see cref="DisplayAssignmentInfo"/> を返すよう <see cref="StagedResult"/> で差し替え可能。
    /// </remarks>
    public sealed class FakeDisplayRoutingService : IDisplayRoutingService
    {
        public readonly struct ActivateCall
        {
            public Camera Camera { get; }
            public DisplayRoutingConfig Config { get; }
            public ActivateCall(Camera camera, DisplayRoutingConfig config)
            {
                Camera = camera;
                Config = config;
            }
        }

        private readonly List<ActivateCall> _calls = new();
        private DisplayAssignmentInfo _lastAssignment;
        private bool _disposed;

        /// <summary>Activate 呼び出し履歴。</summary>
        public IReadOnlyList<ActivateCall> Calls => _calls;

        /// <summary>次回 Activate が返す DisplayAssignmentInfo。null の場合は config から既定の値を構成する。</summary>
        public DisplayAssignmentInfo? StagedResult { get; set; }

        /// <summary>Dispose 済みかどうか。</summary>
        public bool IsDisposed => _disposed;

        /// <inheritdoc />
        public bool IsFallbackActive => _lastAssignment.IsFallbackActive;

        /// <inheritdoc />
        public DisplayAssignmentInfo Activate(Camera camera, DisplayRoutingConfig config)
        {
            _calls.Add(new ActivateCall(camera, config));
            var result = StagedResult ?? new DisplayAssignmentInfo
            {
                RequestedDisplayIndex = config.TargetDisplayIndex,
                EffectiveDisplayIndex = config.TargetDisplayIndex,
                IsFallbackActive = false,
                IsEditorLimitedMode = false,
                DiagnosticMessage = null,
            };
            _lastAssignment = result;
            return result;
        }

        /// <inheritdoc />
        public DisplayAssignmentInfo GetAssignment() => _lastAssignment;

        /// <inheritdoc />
        public void Dispose() => _disposed = true;
    }
}
