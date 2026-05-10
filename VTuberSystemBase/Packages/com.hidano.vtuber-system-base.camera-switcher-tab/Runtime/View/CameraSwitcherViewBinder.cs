#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Subscribes to <see cref="ICameraSwitcherCoordinator.OnStateChanged"/>
    /// and dispatches a <c>Render</c> call to every View. The single binder
    /// keeps the wiring discoverable from the Composition Root.
    /// </summary>
    public sealed class CameraSwitcherViewBinder : IDisposable
    {
        private readonly ICameraSwitcherCoordinator _coordinator;
        private readonly Action _renderAll;
        private bool _disposed;

        public CameraSwitcherViewBinder(ICameraSwitcherCoordinator coordinator, Action renderAll)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _renderAll = renderAll ?? throw new ArgumentNullException(nameof(renderAll));
            _coordinator.OnStateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _coordinator.OnStateChanged -= OnStateChanged;
        }

        private void OnStateChanged()
        {
            if (_disposed) return;
            try { _renderAll(); }
            catch
            {
                // Swallow: a single broken view must not take down the binder.
            }
        }
    }
}
