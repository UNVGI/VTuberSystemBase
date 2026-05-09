#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Stage selection section. Renders the catalog as a list of clickable rows; clicking
    /// a row delegates to <c>SwitchStage</c>. Thumbnails are loaded asynchronously via
    /// <see cref="IAsyncAssetLoader"/> when a key is available; failures fall back to a
    /// placeholder. (Task 6.2, Requirements 3.2, 3.3, 9.6.)
    /// </summary>
    public sealed class StageSelectionSectionView : IDisposable
    {
        public const string ScopeIdSuffix = "stage-selection";

        private readonly VisualElement _root;
        private readonly StageLightingVolumeTabViewModel _viewModel;
        private readonly IAsyncAssetLoader? _assetLoader;
        private readonly string _scopeId;
        private readonly VisualElement _list;

        public StageSelectionSectionView(
            VisualElement root,
            StageLightingVolumeTabViewModel viewModel,
            IAsyncAssetLoader? assetLoader = null,
            string scopeId = ScopeIdSuffix)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _assetLoader = assetLoader;
            _scopeId = scopeId;
            _list = root.Q<VisualElement>("stage-list") ?? throw new InvalidOperationException(
                "Stage selection section missing element 'stage-list'.");

            var unloadBtn = root.Q<Button>("stage-unload");
            if (unloadBtn is not null) unloadBtn.clicked += () => _viewModel.UnloadStage();

            _viewModel.OnStateChanged += Refresh;
            Refresh();
        }

        public void Dispose()
        {
            _viewModel.OnStateChanged -= Refresh;
            _assetLoader?.ReleaseAll(_scopeId);
        }

        private void Refresh()
        {
            _list.Clear();
            foreach (var entry in _viewModel.StageCatalog)
            {
                var row = new VisualElement();
                row.AddToClassList("vsb-slv-stage-list-item");

                var thumb = new VisualElement();
                thumb.AddToClassList("vsb-slv-stage-list-item__thumb");
                row.Add(thumb);

                var label = new Label(entry.DisplayName);
                row.Add(label);

                if (string.Equals(entry.AddressableKey, _viewModel.StageCurrent.AddressableKey,
                    StringComparison.Ordinal))
                {
                    row.AddToClassList("vsb-slv-stage-list-item--current");
                }

                var capturedKey = entry.AddressableKey;
                row.RegisterCallback<ClickEvent>(_ => _viewModel.SwitchStage(capturedKey));
                _list.Add(row);

                if (_assetLoader is not null && entry.ThumbnailAddressableKey is { } thumbKey)
                {
                    _assetLoader.LoadAsync<Texture2D>(thumbKey, _scopeId, result =>
                    {
                        if (result.Success && result.Asset is not null)
                        {
                            thumb.style.backgroundImage = new StyleBackground(result.Asset);
                        }
                    });
                }
            }
        }
    }
}
