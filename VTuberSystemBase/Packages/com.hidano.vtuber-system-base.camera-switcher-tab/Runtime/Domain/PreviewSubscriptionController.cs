#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Owns the <c>camera/preview/command</c> attach / detach lifecycle and
    /// resolves <see cref="PreviewHandleStatePayload.TextureKey"/> values into
    /// concrete handles via <see cref="IPreviewHandleResolver"/>
    /// (Requirement 2.2 / 2.3 / 2.7 / 2.8).
    /// </summary>
    /// <remarks>
    /// Single-threaded; the Coordinator drives every state mutation. Handles
    /// are released on tab deactivation, camera deletion and editing-target
    /// changes.
    /// </remarks>
    public sealed class PreviewSubscriptionController : IDisposable
    {
        public sealed class PreviewSlot
        {
            public CameraId CameraId { get; init; }
            public string? TextureKey { get; set; }
            public object? Handle { get; set; }
            public bool ResolveFailed { get; set; }
            public string? FailureDetail { get; set; }
        }

        private readonly IUiCommandClient _commands;
        private readonly IPreviewHandleResolver _resolver;
        private readonly Dictionary<string, PreviewSlot> _slots = new Dictionary<string, PreviewSlot>(StringComparer.Ordinal);

        private bool _attachActive;
        private int[] _lastSize = new[] { 320, 180 };
        private int _lastFps = 15;

        public event Action<CameraId>? OnSlotChanged;

        public PreviewSubscriptionController(
            IUiCommandClient commands,
            IPreviewHandleResolver resolver)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IReadOnlyDictionary<string, PreviewSlot> Slots => _slots;

        public bool IsAttachActive => _attachActive;

        /// <summary>
        /// Send <c>attach</c> for every cameraId in <paramref name="cameraIds"/>.
        /// Idempotent — calling while already-attached refreshes size / fps but
        /// does not re-issue handle resolution.
        /// </summary>
        public void Attach(IEnumerable<CameraId> cameraIds, int width, int height, int fps)
        {
            var ids = cameraIds.Where(c => c.HasValue).Select(c => c.Value).ToArray();
            if (ids.Length == 0) return;
            _lastSize = new[] { width, height };
            _lastFps = fps;
            foreach (var id in ids)
            {
                if (!_slots.ContainsKey(id))
                {
                    _slots[id] = new PreviewSlot { CameraId = new CameraId(id) };
                }
            }
            _commands.PublishEvent(CameraIpcTopics.PreviewCommand, new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Attach,
                CameraIds = ids,
                Size = new[] { width, height },
                Fps = fps,
            });
            _attachActive = true;
        }

        /// <summary>Send <c>detach</c> and release every cached handle.</summary>
        public void DetachAll()
        {
            if (_slots.Count == 0)
            {
                _attachActive = false;
                return;
            }
            var ids = _slots.Keys.ToArray();
            _commands.PublishEvent(CameraIpcTopics.PreviewCommand, new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Detach,
                CameraIds = ids,
            });
            foreach (var slot in _slots.Values)
            {
                if (slot.TextureKey is not null) _resolver.Release(slot.TextureKey);
            }
            _slots.Clear();
            _attachActive = false;
        }

        /// <summary>Detach a single camera (e.g. on deletion) and release its handle.</summary>
        public void DetachOne(CameraId cameraId)
        {
            if (!cameraId.HasValue) return;
            if (!_slots.TryGetValue(cameraId.Value, out var slot)) return;

            _commands.PublishEvent(CameraIpcTopics.PreviewCommand, new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Detach,
                CameraIds = new[] { cameraId.Value },
            });
            if (slot.TextureKey is not null) _resolver.Release(slot.TextureKey);
            _slots.Remove(cameraId.Value);
            if (_slots.Count == 0) _attachActive = false;
        }

        /// <summary>
        /// Apply an inbound <c>camera/{id}/preview/handle</c> state — call the
        /// resolver to pull the texture handle out of the Service Locator.
        /// </summary>
        public async Task OnHandleStateAsync(CameraId cameraId, PreviewHandleStatePayload payload, CancellationToken cancellationToken = default)
        {
            if (!cameraId.HasValue) return;
            if (!_slots.TryGetValue(cameraId.Value, out var slot))
            {
                // Defensive: create a slot so the View can still pick up the handle.
                slot = new PreviewSlot { CameraId = cameraId };
                _slots[cameraId.Value] = slot;
            }
            slot.TextureKey = payload.TextureKey;

            try
            {
                var resolved = await _resolver.ResolveAsync(payload.TextureKey, cancellationToken).ConfigureAwait(false);
                if (resolved.Found)
                {
                    slot.Handle = resolved.Handle;
                    slot.ResolveFailed = false;
                    slot.FailureDetail = null;
                }
                else
                {
                    slot.Handle = null;
                    slot.ResolveFailed = true;
                    slot.FailureDetail = resolved.FailureDetail;
                }
            }
            catch (Exception ex)
            {
                slot.Handle = null;
                slot.ResolveFailed = true;
                slot.FailureDetail = ex.Message;
            }
            OnSlotChanged?.Invoke(cameraId);
        }

        public void Dispose()
        {
            DetachAll();
        }
    }
}
