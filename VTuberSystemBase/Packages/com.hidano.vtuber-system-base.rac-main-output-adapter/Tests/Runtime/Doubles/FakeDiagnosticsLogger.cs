using System;
using System.Collections.Generic;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IDiagnosticsLogger"/> のテストダブル。<see cref="Log"/> 呼出を内部リストに記録する。
    /// </summary>
    public sealed class FakeDiagnosticsLogger : IDiagnosticsLogger
    {
        private readonly List<Entry> _entries = new();

        /// <inheritdoc/>
        public AdapterLogLevel MinimumLevel { get; set; } = AdapterLogLevel.Trace;

        /// <summary>記録されたログエントリ。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <inheritdoc/>
        public void Log(AdapterLogLevel level, string category, string message, Exception exception = null)
        {
            if (level < MinimumLevel) return;
            _entries.Add(new Entry(level, category, message, exception));
        }

        /// <summary>記録履歴をクリアする。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>指定 category を持つエントリを抽出する。</summary>
        public IEnumerable<Entry> GetByCategory(string category)
        {
            foreach (var e in _entries)
                if (string.Equals(e.Category, category, StringComparison.Ordinal)) yield return e;
        }

        /// <summary>記録された 1 エントリ。</summary>
        public readonly record struct Entry(AdapterLogLevel Level, string Category, string Message, Exception Exception);
    }
}
