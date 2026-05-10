#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Port that publishes UCAPI Flat Records over OSC to the main-output side.
    /// Default adapter wraps hecomi/uOSC; tests substitute a Fake that records
    /// every <see cref="Send"/> call without opening a UDP socket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle:
    /// <list type="number">
    /// <item><see cref="State"/> starts as <see cref="OscEmitterState.Stopped"/>.</item>
    /// <item><see cref="StartAsync"/> moves to <see cref="OscEmitterState.Running"/>;
    /// it is invalid to call <see cref="Send"/> before this completes.</item>
    /// <item><see cref="StopAsync"/> moves back to <see cref="OscEmitterState.Stopped"/>.
    /// <see cref="StartAsync"/> may be called again after a stop.</item>
    /// <item><see cref="IDisposable.Dispose"/> moves to <see cref="OscEmitterState.Disposed"/>;
    /// further <see cref="StartAsync"/> / <see cref="Send"/> calls return failure results.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="Send"/> MUST NOT throw — it returns an <see cref="OscEmitResult"/>
    /// failure if the address is malformed or the adapter is in the wrong state.
    /// Async UDP send failures (e.g. ICMP port unreachable) are surfaced via
    /// <see cref="OnSendFailure"/> on the Unity main thread.
    /// </para>
    /// </remarks>
    public interface IUcapiOscEmitter : IDisposable
    {
        OscEmitterState State { get; }

        /// <summary>Raised on the Unity main thread when an asynchronous send fails.</summary>
        event Action<OscEmitFailure> OnSendFailure;

        /// <summary>Bring the emitter to <see cref="OscEmitterState.Running"/>.</summary>
        Task<OscEmitResult> StartAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>Bring the emitter back to <see cref="OscEmitterState.Stopped"/>.</summary>
        Task<OscEmitResult> StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Enqueue <paramref name="record"/> for transmission to <paramref name="address"/>.
        /// Returns a synchronous failure if the emitter is not running, the address is
        /// malformed, or the record is empty. Successful enqueue does NOT guarantee
        /// delivery — UDP loss is reported via <see cref="OnSendFailure"/>.
        /// </summary>
        OscEmitResult Send(string address, in UcapiFlatRecord record);
    }

    /// <summary>Lifecycle state for <see cref="IUcapiOscEmitter"/>.</summary>
    public enum OscEmitterState
    {
        Stopped = 0,
        Starting,
        Running,
        Stopping,
        Disposed,
    }
}
