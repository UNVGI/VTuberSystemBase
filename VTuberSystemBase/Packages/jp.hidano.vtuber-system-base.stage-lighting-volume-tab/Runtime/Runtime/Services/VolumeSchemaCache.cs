#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Caches the result of the <see cref="StageLightingTopics.VolumeOverrideSchema"/>
    /// request so the Volume Override section UI only fetches the schema once per
    /// connection lifetime. Exposes a retry surface for the failure UI in Requirement 6.9.
    /// See design.md §Services §VolumeSchemaCache (Requirements 6.1, 6.9, 6.11).
    /// </summary>
    public sealed class VolumeSchemaCache
    {
        /// <summary>Empty request payload used by <c>RequestAsync</c>.</summary>
        public readonly struct EmptyRequest { }

        private readonly IUiCommandClient _commandClient;
        private readonly IDiagnosticsLogger? _log;
        private readonly TimeSpan? _timeout;

        public VolumeSchemaCache(
            IUiCommandClient commandClient,
            IDiagnosticsLogger? logger = null,
            TimeSpan? timeout = null)
        {
            _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _log = logger;
            _timeout = timeout;
        }

        public VolumeOverrideSchemaDto? Schema { get; private set; }

        public bool IsLoaded => Schema.HasValue;

        public RequestError? LastError { get; private set; }

        /// <summary>Raised after every <see cref="FetchAsync"/> attempt (success or failure).</summary>
        public event Action? Changed;

        public async Task<bool> FetchAsync(CancellationToken cancellationToken = default)
        {
            if (Schema.HasValue) return true;

            var result = await _commandClient.RequestAsync<EmptyRequest, VolumeOverrideSchemaDto>(
                StageLightingTopics.VolumeOverrideSchema,
                default,
                _timeout,
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                Schema = result.Response;
                LastError = null;
                Changed?.Invoke();
                return true;
            }

            LastError = result.Error;
            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"VolumeSchemaCache fetch failed code={result.Error?.Code}",
                new { code = result.Error?.Code });
            Changed?.Invoke();
            return false;
        }

        /// <summary>Drops the cached schema. The next <see cref="FetchAsync"/> call will hit the transport.</summary>
        public void ResetCache()
        {
            Schema = null;
            LastError = null;
            Changed?.Invoke();
        }
    }
}
