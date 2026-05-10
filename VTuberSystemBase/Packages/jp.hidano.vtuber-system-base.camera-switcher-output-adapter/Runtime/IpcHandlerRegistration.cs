#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;

using CameraSwitcherOutputAdapterCore = VTuberSystemBase.CameraSwitcherOutputAdapter.Domain.CameraSwitcherOutputAdapter;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime
{
    /// <summary>
    /// Wraps the static (non per-camera) <see cref="IOutputCommandDispatcher"/>
    /// registrations for the camera-switcher output adapter. Per-camera dynamic
    /// registrations are owned by <see cref="CameraSwitcherOutputAdapterCore"/> itself.
    /// </summary>
    /// <remarks>
    /// The class is a thin facade over the dispatcher: it stores the returned
    /// <see cref="OutputCommandHandlerRegistration"/> tokens and disposes them in
    /// reverse order on <see cref="Dispose"/>.
    /// </remarks>
    public sealed class IpcHandlerRegistration : IDisposable
    {
        private readonly List<IDisposable> _registrations = new();
        private bool _disposed;

        public int RegisteredHandlerCount => _registrations.Count;

        /// <summary>
        /// Registers the static topics (<c>camera/command</c>,
        /// <c>camera/preview/command</c>, <c>camera/preset/command</c>) to the
        /// adapter's handlers.
        /// </summary>
        public void RegisterAll(IOutputCommandDispatcher dispatcher, CameraSwitcherOutputAdapterCore adapter)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (_disposed) throw new InvalidOperationException("IpcHandlerRegistration disposed");

            _registrations.Add(dispatcher.RegisterEventHandler<CameraCommandPayload>(
                CameraIpcTopics.CameraCommand, adapter.OnCameraCommand));
            _registrations.Add(dispatcher.RegisterEventHandler<PreviewCommandPayload>(
                CameraIpcTopics.PreviewCommand, adapter.OnPreviewCommand));
            _registrations.Add(dispatcher.RegisterEventHandler<PresetCommandPayload>(
                CameraIpcTopics.PresetCommand, adapter.OnPresetCommandObservation));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var i = _registrations.Count - 1; i >= 0; i--)
            {
                try { _registrations[i].Dispose(); } catch { /* defensive */ }
            }
            _registrations.Clear();
        }
    }
}
