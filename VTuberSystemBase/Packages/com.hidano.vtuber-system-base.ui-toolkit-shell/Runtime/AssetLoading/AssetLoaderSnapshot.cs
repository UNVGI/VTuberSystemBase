#nullable enable
using System.Collections.Generic;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Read-only snapshot of the asset-loader's runtime counters; consumed by
    /// <c>ShellDiagnosticsSnapshot</c> (Requirement 4.9) and surfaced via the
    /// diagnostics API. Implementations of <see cref="IAsyncAssetLoader.GetSnapshot"/>
    /// must return a value-type copy that is safe to read off the main thread.
    /// </summary>
    public readonly struct AssetLoaderSnapshot
    {
        public AssetLoaderSnapshot(
            int pendingCount,
            int completedCount,
            int failedCount,
            IReadOnlyDictionary<string, int> pendingByScope)
        {
            PendingCount = pendingCount;
            CompletedCount = completedCount;
            FailedCount = failedCount;
            PendingByScope = pendingByScope ?? EmptyScopeMap;
        }

        public int PendingCount { get; }

        public int CompletedCount { get; }

        public int FailedCount { get; }

        public IReadOnlyDictionary<string, int> PendingByScope { get; }

        public static AssetLoaderSnapshot Empty { get; } =
            new AssetLoaderSnapshot(0, 0, 0, EmptyScopeMap);

        private static readonly IReadOnlyDictionary<string, int> EmptyScopeMap =
            new Dictionary<string, int>();
    }
}
