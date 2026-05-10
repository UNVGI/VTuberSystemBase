#nullable enable
using System;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Drives 1-frame-1-message OSC publication for the editing camera
    /// (Requirement 4.4 / 4.5 / 4.11 / 4.12). The Coordinator owns the
    /// instance, calls <see cref="SetTarget"/> when the user picks a different
    /// camera and <see cref="OnCameraDeleted"/> when a camera is removed; the
    /// LateUpdate driver calls <see cref="FrameTick"/> once per frame with a
    /// fresh <see cref="CameraSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// Per <c>FrameTick</c> at most one <see cref="IUcapiOscEmitter.Send"/> is
    /// issued. Failures are recorded against <see cref="FailureAggregator"/>
    /// and never propagated as exceptions (Requirement 12.1 / 12.4).
    /// </remarks>
    public sealed class OscStreamController
    {
        private readonly IUcapiFlatRecordSerializer _serializer;
        private readonly IUcapiOscEmitter _emitter;
        private readonly FailureAggregator _failures;
        private readonly ITimeProvider _time;
        private readonly string _addressPrefix;

        private CameraId _target;
        private long _sentCount;
        private long _skippedCount;

        public OscStreamController(
            IUcapiFlatRecordSerializer serializer,
            IUcapiOscEmitter emitter,
            FailureAggregator failures,
            ITimeProvider time,
            string addressPrefix = OscAddressBuilder.DefaultPrefix)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _addressPrefix = addressPrefix ?? OscAddressBuilder.DefaultPrefix;
            _emitter.OnSendFailure += OnEmitterSendFailure;
        }

        public CameraId Target => _target;
        public long SentCount => _sentCount;
        public long SkippedCount => _skippedCount;

        public void SetTarget(CameraId? target)
        {
            _target = target.GetValueOrDefault();
        }

        public void OnCameraDeleted(CameraId deleted)
        {
            if (_target.HasValue && deleted.HasValue
                && string.Equals(_target.Value, deleted.Value, StringComparison.Ordinal))
            {
                _target = default;
            }
        }

        /// <summary>
        /// Called once per LateUpdate by the tick driver. <paramref name="snapshot"/>
        /// must already have <see cref="CameraSnapshot.CameraId"/> set to the
        /// current target; pass <c>null</c> when no Camera reference is available
        /// (the controller skips the frame in that case).
        /// </summary>
        public OscEmitResult FrameTick(in CameraSnapshot? snapshot)
        {
            if (!_target.HasValue) return OscEmitResult.Ok();
            if (snapshot is null)
            {
                _skippedCount++;
                return OscEmitResult.Ok();
            }
            var snap = snapshot.Value;
            if (!snap.CameraId.HasValue
                || !string.Equals(snap.CameraId.Value, _target.Value, StringComparison.Ordinal))
            {
                _skippedCount++;
                return OscEmitResult.Ok();
            }

            SerializeResult serialized;
            try
            {
                serialized = _serializer.Serialize(snap);
            }
            catch (Exception ex)
            {
                _skippedCount++;
                _failures.Record(FailureKind.OscFailure, $"serialize threw: {ex.Message}", _time.UtcNow);
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.SerializeFailed, ex.Message, ex));
            }
            if (!serialized.Success)
            {
                _skippedCount++;
                _failures.Record(FailureKind.OscFailure,
                    $"serialize invalid: {serialized.FailureReason} {serialized.FailureDetail}", _time.UtcNow);
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.SerializeFailed,
                    serialized.FailureReason.ToString()));
            }

            string address;
            try
            {
                address = OscAddressBuilder.BuildFlatAddress(_addressPrefix, _target);
            }
            catch (Exception ex)
            {
                _skippedCount++;
                _failures.Record(FailureKind.OscFailure, $"address build failed: {ex.Message}", _time.UtcNow);
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InvalidAddress, ex.Message, ex));
            }

            var result = _emitter.Send(address, serialized.Record);
            if (result.Success)
            {
                _sentCount++;
            }
            else
            {
                _skippedCount++;
                if (result.Failure is { } f)
                {
                    _failures.Record(FailureKind.OscFailure,
                        $"send failed: {f.Kind} {f.Detail}", _time.UtcNow);
                }
            }
            return result;
        }

        public void Dispose()
        {
            _emitter.OnSendFailure -= OnEmitterSendFailure;
        }

        private void OnEmitterSendFailure(OscEmitFailure failure)
        {
            _failures.Record(FailureKind.OscFailure,
                $"async send failed: {failure.Kind} {failure.Detail}", _time.UtcNow);
        }
    }
}
