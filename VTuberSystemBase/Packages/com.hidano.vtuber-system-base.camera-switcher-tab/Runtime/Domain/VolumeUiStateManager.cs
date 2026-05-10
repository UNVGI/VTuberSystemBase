#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Caches the Volume override schema per cameraId, fans inbound
    /// <c>volume/*</c> state changes into observable per-camera caches, and
    /// suppresses echo-during-drag so the UI does not jitter while the user is
    /// actively manipulating a parameter (Requirement 8.10 / 8.11 / 8.13).
    /// </summary>
    /// <remarks>
    /// Single-threaded (Coordinator's main-thread invariant). The caller is
    /// responsible for actually issuing the IPC <c>request</c> for the schema —
    /// this class only stores the result; it never sends commands itself.
    /// </remarks>
    public sealed class VolumeUiStateManager
    {
        public sealed class CameraVolumeState
        {
            public CameraId CameraId { get; init; }
            public VolumeMetadataResponse? Schema { get; set; }
            public bool SchemaFailed { get; set; }
            public string? SchemaFailureDetail { get; set; }
            public bool VolumeEnabled { get; set; } = true;

            public Dictionary<string, bool> OverrideEnabled { get; }
                = new Dictionary<string, bool>(StringComparer.Ordinal);

            public Dictionary<(string overrideType, string param), JsonElement> ParamValues { get; }
                = new Dictionary<(string, string), JsonElement>();

            public Dictionary<string, JsonElement> PendingEchoSuppressed { get; }
                = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        private readonly IUiCommandClient _commands;
        private readonly FailureAggregator _failures;
        private readonly ITimeProvider _time;
        private readonly TimeSpan _requestTimeout;

        private readonly Dictionary<string, CameraVolumeState> _states = new Dictionary<string, CameraVolumeState>(StringComparer.Ordinal);

        // Drag suppression keyed by (overrideType, param). Only the editing
        // camera is dragged at any one time (D-3); the cameraId is implicit.
        private readonly HashSet<(string overrideType, string param)> _dragging = new HashSet<(string, string)>();

        public event Action<CameraId>? OnCameraStateChanged;

        public VolumeUiStateManager(
            IUiCommandClient commands,
            FailureAggregator failures,
            ITimeProvider time,
            TimeSpan? requestTimeout = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        }

        /// <summary>True if a state has been seeded for <paramref name="cameraId"/>.</summary>
        public bool HasState(CameraId cameraId) => cameraId.HasValue && _states.ContainsKey(cameraId.Value);

        public CameraVolumeState GetOrCreate(CameraId cameraId)
        {
            if (!cameraId.HasValue) throw new ArgumentException("cameraId unset", nameof(cameraId));
            if (!_states.TryGetValue(cameraId.Value, out var state))
            {
                state = new CameraVolumeState { CameraId = cameraId };
                _states[cameraId.Value] = state;
            }
            return state;
        }

        public bool TryGet(CameraId cameraId, out CameraVolumeState state)
        {
            if (cameraId.HasValue && _states.TryGetValue(cameraId.Value, out var s))
            {
                state = s;
                return true;
            }
            state = default!;
            return false;
        }

        public void Forget(CameraId cameraId)
        {
            if (cameraId.HasValue) _states.Remove(cameraId.Value);
        }

        // ---- Drag suppression ----

        public bool IsUserDragging(string overrideType, string param)
            => _dragging.Contains((overrideType, param));

        public void BeginDrag(string overrideType, string param) => _dragging.Add((overrideType, param));

        public void EndDrag(string overrideType, string param)
        {
            _dragging.Remove((overrideType, param));
        }

        /// <summary>
        /// Called when the editing target changes. Issues a metadata
        /// <c>request</c> to fetch the schema if one is not already cached;
        /// failures are aggregated but do NOT take down the tab.
        /// </summary>
        public async Task OnEditTargetChangedAsync(CameraId cameraId, CancellationToken cancellationToken = default)
        {
            if (!cameraId.HasValue) return;
            var state = GetOrCreate(cameraId);
            if (state.Schema is not null) return; // already cached

            var topic = CameraIpcTopics.VolumeOverridesMetadata(cameraId);
            var req = new VolumeMetadataRequest { CameraId = cameraId.Value };
            var result = await _commands.RequestAsync<VolumeMetadataRequest, VolumeMetadataResponse>(
                topic, req, _requestTimeout, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                state.Schema = result.Response;
                state.SchemaFailed = false;
                state.SchemaFailureDetail = null;
            }
            else
            {
                state.SchemaFailed = true;
                state.SchemaFailureDetail = result.Error?.Code.ToString();
                _failures.Record(FailureKind.VolumeMetadataFailure,
                    $"metadata request failed for {cameraId.Value}: {result.Error?.Code} {result.Error?.Detail}",
                    _time.UtcNow);
            }
            OnCameraStateChanged?.Invoke(cameraId);
        }

        // ---- Inbound state application ----

        public void ApplyVolumeEnabledState(CameraId cameraId, bool enabled)
        {
            if (!cameraId.HasValue) return;
            var state = GetOrCreate(cameraId);
            state.VolumeEnabled = enabled;
            OnCameraStateChanged?.Invoke(cameraId);
        }

        public void ApplyOverridesListState(CameraId cameraId, IReadOnlyList<VolumeOverrideEntry> overrides)
        {
            if (!cameraId.HasValue) return;
            var state = GetOrCreate(cameraId);
            state.OverrideEnabled.Clear();
            foreach (var entry in overrides)
            {
                state.OverrideEnabled[entry.Type] = entry.Enabled;
            }
            OnCameraStateChanged?.Invoke(cameraId);
        }

        public void ApplyOverrideEnabledState(CameraId cameraId, string overrideType, bool enabled)
        {
            if (!cameraId.HasValue) return;
            var state = GetOrCreate(cameraId);
            state.OverrideEnabled[overrideType] = enabled;
            OnCameraStateChanged?.Invoke(cameraId);
        }

        /// <summary>
        /// Apply a parameter value echo. Suppresses the assignment if the user
        /// is currently dragging this param (Requirement 8.10), and records
        /// the latest received value in <see cref="CameraVolumeState.PendingEchoSuppressed"/>
        /// so EndDrag can surface it.
        /// </summary>
        public void ApplyOverrideParamState(CameraId cameraId, string overrideType, string param, JsonElement value)
        {
            if (!cameraId.HasValue) return;
            var state = GetOrCreate(cameraId);
            if (IsUserDragging(overrideType, param))
            {
                // Suppress echo while dragging — keep the latest server value
                // available for EndDrag handling by the View layer.
                state.PendingEchoSuppressed[overrideType + "/" + param] = value;
                return;
            }
            state.ParamValues[(overrideType, param)] = value;
            OnCameraStateChanged?.Invoke(cameraId);
        }

        /// <summary>
        /// Pull the most recent suppressed echo (if any) for
        /// (<paramref name="overrideType"/>, <paramref name="param"/>) and apply
        /// it. Called by the View when the user releases the mouse.
        /// </summary>
        public bool TryFlushSuppressedEcho(CameraId cameraId, string overrideType, string param, out JsonElement value)
        {
            value = default;
            if (!cameraId.HasValue) return false;
            if (!_states.TryGetValue(cameraId.Value, out var state)) return false;
            var key = overrideType + "/" + param;
            if (!state.PendingEchoSuppressed.TryGetValue(key, out value)) return false;
            state.PendingEchoSuppressed.Remove(key);
            state.ParamValues[(overrideType, param)] = value;
            OnCameraStateChanged?.Invoke(cameraId);
            return true;
        }
    }
}
