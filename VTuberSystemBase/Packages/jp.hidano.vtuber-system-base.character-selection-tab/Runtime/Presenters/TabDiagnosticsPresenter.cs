#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Diagnostics;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    /// <summary>
    /// Renders <see cref="TabDiagnosticsSnapshot"/> in the tab footer. (task 5.6.)
    /// Updates are throttled to 1 second so high-frequency state churn does not
    /// reflow the diagnostics row repeatedly.
    /// </summary>
    public sealed class TabDiagnosticsPresenter : IDisposable
    {
        public const string LabelName = "vsb-char-tab__diagnostics__label";

        private readonly ICharacterTabDiagnostics _diagnostics;
        private readonly ICharacterTabStateStore _store;
        private readonly IPresetStoreLogic _presets;
        private readonly IClock _clock;
        private readonly TimeSpan _throttle;
        private readonly VisualElement _container;
        private readonly Label _label;
        private DateTimeOffset _lastRenderedAt;
        private bool _renderPending;
        private bool _disposed;

        public TabDiagnosticsPresenter(
            ICharacterTabDiagnostics diagnostics,
            ICharacterTabStateStore store,
            IPresetStoreLogic presets,
            IClock clock,
            VisualElement container,
            TimeSpan? throttle = null)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _throttle = throttle ?? TimeSpan.FromSeconds(1);
            _label = new Label { name = LabelName };
            _container.Add(_label);
            _store.OnChanged += OnStoreChanged;
            _presets.OnSaved += OnSaved;
            _clock.OnTick += OnTick;
            Render();
        }

        public TabDiagnosticsSnapshot LastSnapshot { get; private set; }
        public int RenderCountForTesting { get; private set; }

        public void Render()
        {
            LastSnapshot = _diagnostics.Capture();
            RenderCountForTesting++;
            _label.text =
                $"slots={LastSnapshot.TotalSlotCount} assigned={LastSnapshot.AssignedSlotCount} " +
                $"errors={LastSnapshot.ErrorSlotCount} inflight={LastSnapshot.InFlightOperationCount} " +
                $"conn={LastSnapshot.ConnectionStatus} active={LastSnapshot.ActivePresetId ?? "-"}";
            _lastRenderedAt = _clock.UtcNow;
            _renderPending = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            _presets.OnSaved -= OnSaved;
            _clock.OnTick -= OnTick;
            _container.Remove(_label);
        }

        // ---------- private ----------

        private void OnStoreChanged(StateChangeScope scope) => RequestRender();
        private void OnSaved(PresetSavedEvent e) => RequestRender();

        private void RequestRender()
        {
            // Throttle: render immediately if we are past the cooldown,
            // otherwise mark pending so the next OnTick can flush.
            var now = _clock.UtcNow;
            if (now - _lastRenderedAt >= _throttle)
            {
                Render();
            }
            else
            {
                _renderPending = true;
            }
        }

        private void OnTick(DateTimeOffset now)
        {
            if (!_renderPending) return;
            if (now - _lastRenderedAt < _throttle) return;
            Render();
        }
    }
}
