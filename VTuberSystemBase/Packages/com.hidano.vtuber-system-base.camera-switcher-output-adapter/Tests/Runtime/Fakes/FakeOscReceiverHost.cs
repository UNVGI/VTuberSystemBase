#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="IOscReceiverHost"/>. Records lifecycle calls and
    /// allows tests to manually emit <see cref="OscReceivedMessage"/> via
    /// <see cref="Emit"/> after a successful <see cref="StartAsync"/>.
    /// </summary>
    public sealed class FakeOscReceiverHost : IOscReceiverHost
    {
        public OscReceiverStartResult? NextStartResult { get; set; }

        public string? LastHost { get; private set; }
        public int? LastPort { get; private set; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public OscReceiverHostStatus Status { get; private set; } = OscReceiverHostStatus.Stopped;
        public event Action<OscReceivedMessage>? MessageReceived;

        public Task<OscReceiverStartResult> StartAsync(string host, int port, CancellationToken ct = default)
        {
            StartCallCount++;
            LastHost = host;
            LastPort = port;
            Status = OscReceiverHostStatus.Starting;
            var result = NextStartResult ?? OscReceiverStartResult.Ok();
            Status = result.Success ? OscReceiverHostStatus.Running : OscReceiverHostStatus.Failed;
            return Task.FromResult(result);
        }

        public Task StopAsync()
        {
            StopCallCount++;
            Status = OscReceiverHostStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCallCount++;
            Status = OscReceiverHostStatus.Stopped;
        }

        /// <summary>Test-side helper: emit a message as if the OSC server received it.</summary>
        public void Emit(string cameraId, byte[] blob)
        {
            MessageReceived?.Invoke(new OscReceivedMessage(cameraId, blob));
        }
    }
}
