#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Osc
{
    /// <summary>
    /// Owns the host / port configuration, default fallback (<c>127.0.0.1:57300</c>),
    /// and the <see cref="IUcapiOscEmitter.StartAsync"/> /
    /// <see cref="IUcapiOscEmitter.StopAsync"/> lifecycle of an
    /// <see cref="IUcapiOscEmitter"/> (Requirement 4.7 / 4.8 / 4.9 / 10.8).
    /// </summary>
    /// <remarks>
    /// The lifecycle is driven explicitly by the Composition Root — this class
    /// does NOT subscribe to <c>IConnectionStatus</c> directly so it remains
    /// engine-free and unit-testable. The Coordinator decides when to call
    /// <see cref="StartAsync"/> / <see cref="StopAsync"/> based on tab activation
    /// and IPC connection state.
    /// </remarks>
    public sealed class OscClientLifecycle : IDisposable
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 57300;

        private readonly IUcapiOscEmitter _emitter;
        private readonly IDiagnosticsLogger? _log;
        private readonly object _gate = new object();
        private string _host;
        private int _port;
        private bool _disposed;

        public OscClientLifecycle(
            IUcapiOscEmitter emitter,
            string? host = null,
            int? port = null,
            IDiagnosticsLogger? logger = null)
        {
            _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
            _host = string.IsNullOrEmpty(host) ? DefaultHost : host!;
            _port = port is > 0 and <= 65535 ? port.Value : DefaultPort;
            _log = logger;
        }

        public string Host => _host;
        public int Port => _port;
        public OscEmitterState EmitterState => _emitter.State;

        /// <summary>Override the host / port. Effective on the next <see cref="StartAsync"/>.</summary>
        public void Configure(string host, int port)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentException("host is empty", nameof(host));
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            lock (_gate)
            {
                _host = host;
                _port = port;
            }
        }

        public async Task<OscEmitResult> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed));
            string host;
            int port;
            lock (_gate)
            {
                host = _host;
                port = _port;
            }
            _log?.Log(LogLevel.Info, LogCategory.TabSpec, $"OSC.Start host={host} port={port}");
            var result = await _emitter.StartAsync(host, port, cancellationToken).ConfigureAwait(false);
            if (!result.Success && result.Failure is { } f)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"OSC.Start failed kind={f.Kind} detail={f.Detail}");
            }
            return result;
        }

        public async Task<OscEmitResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed));
            _log?.Log(LogLevel.Info, LogCategory.TabSpec, "OSC.Stop");
            return await _emitter.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _emitter.Dispose(); } catch { /* defensive */ }
        }
    }
}
