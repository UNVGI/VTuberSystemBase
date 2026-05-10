#nullable enable
using System;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    /// <summary>
    /// Routes incoming <see cref="OscReceivedMessage"/> values to a registered
    /// <see cref="CameraEntry"/> by cameraId. Calls <paramref name="apply"/> with
    /// the resolved entry on success, or <paramref name="onUnknownCameraId"/> when
    /// the registry has no entry for the cameraId carried in the message.
    /// </summary>
    /// <remarks>
    /// The router intentionally does not depend on <c>FailureAggregator</c> directly;
    /// callers wire the failure callback to <c>FailureAggregator.RecordUnknownCameraIdOnOsc</c>
    /// in production and to a test buffer in unit tests.
    /// </remarks>
    public sealed class OscMessageRouter
    {
        private readonly Func<CameraId, CameraEntry?> _tryResolve;
        private readonly Action<string> _onUnknownCameraId;

        public OscMessageRouter(
            Func<CameraId, CameraEntry?> tryResolve,
            Action<string> onUnknownCameraId)
        {
            _tryResolve = tryResolve ?? throw new ArgumentNullException(nameof(tryResolve));
            _onUnknownCameraId = onUnknownCameraId ?? throw new ArgumentNullException(nameof(onUnknownCameraId));
        }

        /// <summary>
        /// Resolves the cameraId carried in <paramref name="message"/> via the
        /// configured registry callback and invokes <paramref name="apply"/> with
        /// the matching entry. If the cameraId is unknown the unknown-cameraId
        /// callback fires and <paramref name="apply"/> is not invoked.
        /// </summary>
        public void Route(in OscReceivedMessage message, Action<CameraEntry, byte[]> apply)
        {
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            // OscReceivedMessage guarantees a valid cameraId string class.
            if (!CameraId.TryCreate(message.CameraId, out var cameraId))
            {
                _onUnknownCameraId(message.CameraId);
                return;
            }

            var entry = _tryResolve(cameraId);
            if (entry == null)
            {
                _onUnknownCameraId(message.CameraId);
                return;
            }

            apply(entry, message.Blob);
        }
    }
}
