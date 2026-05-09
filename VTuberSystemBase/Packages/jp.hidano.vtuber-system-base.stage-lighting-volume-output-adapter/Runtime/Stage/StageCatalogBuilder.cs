#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage
{
    /// <summary>
    /// Builds the <see cref="StageCatalogDto"/> by querying the
    /// <see cref="IInstantiationProvider"/> for resource locations under the
    /// <c>"stage"</c> Addressables label.
    /// </summary>
    public sealed class StageCatalogBuilder
    {
        public const string DefaultLabel = "stage";
        public const string ThumbnailKeySuffix = ".thumbnail";

        private readonly AdapterLogger? _logger;
        private readonly string _label;

        public StageCatalogBuilder() : this(null, DefaultLabel) { }
        internal StageCatalogBuilder(AdapterLogger? logger, string? label = null)
        {
            _logger = logger;
            _label = string.IsNullOrEmpty(label) ? DefaultLabel : label!;
        }

        public async Task<StageCatalogDto> BuildAsync(IInstantiationProvider provider, CancellationToken ct = default)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            IReadOnlyList<string> primaryKeys;
            try
            {
                primaryKeys = await provider.LoadResourceLocationsAsync(_label, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error("StageCatalogBuilder", "load_failed",
                    context: ex.Message, exception: ex);
                return new StageCatalogDto(Array.Empty<StageCatalogEntryDto>());
            }

            if (primaryKeys == null || primaryKeys.Count == 0)
            {
                _logger?.Warning("StageCatalogBuilder", "label_not_found",
                    context: $"Stage label '{_label}' returned no locations.");
                return new StageCatalogDto(Array.Empty<StageCatalogEntryDto>());
            }

            var items = new List<StageCatalogEntryDto>(primaryKeys.Count);
            foreach (var primary in primaryKeys)
            {
                if (string.IsNullOrEmpty(primary)) continue;
                items.Add(new StageCatalogEntryDto(
                    AddressableKey: primary,
                    DisplayName: primary,
                    ThumbnailAddressableKey: primary + ThumbnailKeySuffix));
            }
            return new StageCatalogDto(items);
        }
    }
}
