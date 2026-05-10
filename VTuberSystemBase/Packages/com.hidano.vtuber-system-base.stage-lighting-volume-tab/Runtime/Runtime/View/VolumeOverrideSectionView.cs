#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Volume Override editor section. Builds dynamic UI from the cached
    /// <see cref="VolumeOverrideSchemaDto"/>: each Override gets an enabled toggle plus
    /// param controls produced by <see cref="IVolumeOverrideParamFactory"/>. Provides a
    /// retry button when the schema fetch failed.
    /// (Task 6.5, Requirements 6.2-6.11.)
    /// </summary>
    public sealed class VolumeOverrideSectionView : IDisposable
    {
        private readonly VisualElement _root;
        private readonly StageLightingVolumeTabViewModel _viewModel;
        private readonly IVolumeOverrideParamFactory _factory;
        private readonly VisualElement _list;

        public VolumeOverrideSectionView(
            VisualElement root,
            StageLightingVolumeTabViewModel viewModel,
            IVolumeOverrideParamFactory factory)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _list = root.Q<VisualElement>("volume-override-list") ?? throw new InvalidOperationException(
                "Volume section missing element 'volume-override-list'.");
            var retryBtn = root.Q<Button>("volume-schema-retry");
            if (retryBtn is not null)
                retryBtn.clicked += async () => await _viewModel.RetryVolumeSchemaFetchAsync();

            _viewModel.OnStateChanged += Refresh;
            Refresh();
        }

        public void Dispose()
        {
            _viewModel.OnStateChanged -= Refresh;
        }

        private void Refresh()
        {
            _list.Clear();
            var schema = _viewModel.VolumeSchema;
            if (schema is null)
            {
                _list.Add(new Label("Volume schema unavailable. Tap retry."));
                return;
            }
            foreach (var type in schema.Value.Types)
            {
                var typeBox = new VisualElement();
                typeBox.AddToClassList("vsb-slv-volume-list-item");

                var enabled = _viewModel.VolumeOverrideEnabled.TryGetValue(type.TypeFullName, out var en) && en;
                var toggle = new Toggle(type.DisplayName) { value = enabled };
                var capturedFullName = type.TypeFullName;
                toggle.RegisterValueChangedCallback(e =>
                    _viewModel.SetVolumeOverrideEnabled(capturedFullName, e.newValue));
                typeBox.Add(toggle);

                if (type.Params is not null)
                {
                    foreach (var p in type.Params)
                    {
                        var current = _viewModel.VolumeParamValues.TryGetValue(
                            (type.TypeFullName, p.ParamName), out var v)
                            ? v
                            : p.DefaultValue;
                        var capturedTypeName = type.TypeFullName;
                        var capturedParamName = p.ParamName;
                        var control = _factory.CreateControl(p, current, value =>
                            _viewModel.UpdateVolumeOverrideParam(capturedTypeName, capturedParamName, value));
                        if (control is not null) typeBox.Add(control);
                    }
                }

                _list.Add(typeBox);
            }
        }
    }
}
