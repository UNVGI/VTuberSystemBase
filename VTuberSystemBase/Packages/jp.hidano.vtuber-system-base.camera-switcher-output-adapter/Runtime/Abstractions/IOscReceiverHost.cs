#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Lifecycle and event surface of the OSC receiver. The default implementation
    /// wraps <c>uOSC.uOscServer</c> attached to a dedicated GameObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Event delivery contract</strong> (CSO-3, Requirement 1.8): implementations
    /// MUST raise <see cref="MessageReceived"/> on the Unity main thread only. The
    /// default <c>UoscReceiverHostAdapter</c> achieves this by relying on
    /// <c>uOscServer.onDataReceived</c>'s Update-driven dispatch.
    /// </para>
    /// <para>
    /// <strong>Pre/post conditions</strong>:
    /// <list type="bullet">
    /// <item><see cref="MessageReceived"/> MUST NOT fire before
    /// <see cref="StartAsync"/> returns <see cref="OscReceiverStartResult.Success"/>=true,
    /// nor after <see cref="StopAsync"/> returns.</item>
    /// <item>After a failed <see cref="StartAsync"/> the host transitions to
    /// <see cref="OscReceiverHostStatus.Failed"/>; a subsequent <see cref="StartAsync"/>
    /// is allowed and re-attempts the bind.</item>
    /// <item>After <see cref="StopAsync"/> the host returns to
    /// <see cref="OscReceiverHostStatus.Stopped"/> and a subsequent
    /// <see cref="StartAsync"/> is allowed.</item>
    /// <item>Implementations MUST drop messages whose address fails the cameraId
    /// extraction (mismatched prefix / invalid cameraId character class) without
    /// raising <see cref="MessageReceived"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IOscReceiverHost : IDisposable
    {
        /// <summary>Current lifecycle status. Mutates only on the main thread.</summary>
        OscReceiverHostStatus Status { get; }

        /// <summary>
        /// Raised once per accepted OSC message (post cameraId / blob validation).
        /// Subscribers MUST NOT retain <see cref="OscReceivedMessage.Blob"/> past the
        /// synchronous handler call (CSO-4).
        /// </summary>
        event Action<OscReceivedMessage>? MessageReceived;

        /// <summary>
        /// Binds the underlying OSC server to <paramref name="host"/>:<paramref name="port"/>.
        /// </summary>
        /// <param name="host">IPv4 host string (e.g. <c>127.0.0.1</c>).</param>
        /// <param name="port">UDP port in the [0, 65535] range.</param>
        /// <param name="ct">Cancellation token; cancellation MAY be honoured but is not required.</param>
        /// <returns>
        /// <see cref="OscReceiverStartResult.Ok"/> on a successful bind; otherwise
        /// <see cref="OscReceiverStartResult.Failure"/> with a populated detail string.
        /// On failure the host transitions to <see cref="OscReceiverHostStatus.Failed"/>.
        /// </returns>
        Task<OscReceiverStartResult> StartAsync(string host, int port, CancellationToken ct = default);

        /// <summary>
        /// Stops the underlying server, releases the UDP socket, and tears down the
        /// host GameObject (when applicable). After completion the host is in
        /// <see cref="OscReceiverHostStatus.Stopped"/>.
        /// </summary>
        Task StopAsync();
    }
}
