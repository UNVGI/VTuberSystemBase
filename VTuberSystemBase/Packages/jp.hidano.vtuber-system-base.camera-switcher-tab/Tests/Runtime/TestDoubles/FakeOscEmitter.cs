#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IUcapiOscEmitter"/>. Records every send (address +
    /// blob length + a copy of the bytes) for assertions and exposes
    /// <see cref="RaiseSendFailure"/> so tests can simulate UDP errors.
    /// </summary>
    public sealed class FakeOscEmitter : IUcapiOscEmitter
    {
        public sealed class SendRecord
        {
            public string Address { get; init; } = "";
            public int Length { get; init; }
            public byte[] Bytes { get; init; } = Array.Empty<byte>();
        }

        private OscEmitterState _state = OscEmitterState.Stopped;

        public OscEmitterState State => _state;

        public List<SendRecord> Sent { get; } = new List<SendRecord>();
        public string? LastHost { get; private set; }
        public int LastPort { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public bool ForceStartFailure { get; set; }

        public event Action<OscEmitFailure>? OnSendFailure;

        public Task<OscEmitResult> StartAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (_state == OscEmitterState.Disposed)
                return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed)));
            if (ForceStartFailure)
            {
                _state = OscEmitterState.Stopped;
                return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InitializationFailed)));
            }
            _state = OscEmitterState.Running;
            LastHost = host;
            LastPort = port;
            return Task.FromResult(OscEmitResult.Ok());
        }

        public Task<OscEmitResult> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            if (_state == OscEmitterState.Disposed)
                return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed)));
            _state = OscEmitterState.Stopped;
            return Task.FromResult(OscEmitResult.Ok());
        }

        public OscEmitResult Send(string address, in UcapiFlatRecord record)
        {
            if (_state == OscEmitterState.Disposed)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed));
            if (_state != OscEmitterState.Running)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.NotStarted));
            if (string.IsNullOrEmpty(address))
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InvalidAddress));
            if (!record.HasValue || record.Length == 0)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.SerializeFailed));
            var bytes = record.AsBytes();
            var copy = new byte[bytes.Length];
            Array.Copy(bytes, copy, bytes.Length);
            Sent.Add(new SendRecord { Address = address, Length = bytes.Length, Bytes = copy });
            return OscEmitResult.Ok();
        }

        public void Dispose()
        {
            _state = OscEmitterState.Disposed;
        }

        /// <summary>Manually invoke <see cref="OnSendFailure"/> to simulate UDP errors.</summary>
        public void RaiseSendFailure(OscEmitFailure failure)
        {
            OnSendFailure?.Invoke(failure);
        }
    }
}
