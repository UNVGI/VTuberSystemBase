#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    /// <summary>
    /// CRUD UI for presets, with a single bar containing dropdown / name input
    /// and 5 action buttons. (task 5.5.) Validation errors surface to a label
    /// inside the bar without throwing. Activate fans out via
    /// <see cref="IPresetRestoreOrchestrator.ReplayActivePresetAsync"/> so the
    /// switch goes through the normal state path.
    /// </summary>
    public sealed class PresetManagerPresenter : IDisposable
    {
        public const string ActiveLabelName = "vsb-preset-bar__active";
        public const string DropdownName = "vsb-preset-bar__dropdown";
        public const string NameInputName = "vsb-preset-bar__name-input";
        public const string CreateBtnName = "vsb-preset-bar__create-btn";
        public const string RenameBtnName = "vsb-preset-bar__rename-btn";
        public const string DuplicateBtnName = "vsb-preset-bar__duplicate-btn";
        public const string DeleteBtnName = "vsb-preset-bar__delete-btn";
        public const string ActivateBtnName = "vsb-preset-bar__activate-btn";
        public const string ErrorLabelName = "vsb-preset-bar__error";

        private readonly IPresetStoreLogic _logic;
        private readonly ICharacterTabStateStore _store;
        private readonly IPresetRestoreOrchestrator _restore;
        private readonly VisualElement _container;
        private readonly VisualTreeAsset? _barTemplate;
        private readonly IDiagnosticsLogger? _log;

        private VisualElement? _root;
        private DropdownField? _dropdown;
        private TextField? _nameInput;
        private Label? _activeLabel;
        private Label? _errorLabel;
        private bool _disposed;

        public PresetManagerPresenter(
            IPresetStoreLogic logic,
            ICharacterTabStateStore store,
            IPresetRestoreOrchestrator restore,
            VisualElement container,
            VisualTreeAsset? barTemplate,
            IDiagnosticsLogger? logger = null)
        {
            _logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _restore = restore ?? throw new ArgumentNullException(nameof(restore));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _barTemplate = barTemplate;
            _log = logger;

            _logic.OnSaved += OnSaved;
            _logic.OnLoaded += OnLoaded;
            BuildBar();
            RenderPresetBar();
        }

        public string? LastErrorMessage { get; private set; }

        public void RenderPresetBar()
        {
            if (_root is null) return;
            var headers = _logic.ListPresets();
            var names = new List<string>(headers.Count);
            foreach (var h in headers) names.Add(h.Name);
            if (_dropdown is not null)
            {
                _dropdown.choices = names;
                if (names.Count > 0 && (_dropdown.value is null || !names.Contains(_dropdown.value)))
                {
                    _dropdown.value = names[0];
                }
                else if (names.Count == 0)
                {
                    _dropdown.value = null;
                }
            }
            if (_activeLabel is not null)
            {
                if (_logic.ActivePresetId is null)
                {
                    _activeLabel.text = "(no active preset)";
                }
                else
                {
                    foreach (var h in headers)
                    {
                        if (string.Equals(h.PresetId, _logic.ActivePresetId, StringComparison.Ordinal))
                        {
                            _activeLabel.text = $"Active: {h.Name}";
                            break;
                        }
                    }
                }
            }
        }

        public Task<PresetOperationResult> CreatePresetAsync(string newName)
            => HandleResultAsync(_logic.CreateAsync(newName));

        public Task<PresetOperationResult> RenamePresetAsync(string presetId, string newName)
            => HandleResultAsync(_logic.RenameAsync(presetId, newName));

        public Task<PresetOperationResult> DuplicatePresetAsync(string presetId, string newName)
            => HandleResultAsync(_logic.DuplicateAsync(presetId, newName));

        public Task<PresetOperationResult> DeletePresetAsync(string presetId)
            => HandleResultAsync(_logic.DeleteAsync(presetId));

        public async Task<PresetOperationResult> ActivatePresetAsync(string presetId)
        {
            var result = await _logic.SetActiveAsync(presetId).ConfigureAwait(false);
            if (!result.Success)
            {
                ShowError(result);
                RenderPresetBar();
                return result;
            }
            _store.SetActivePreset(presetId);
            try
            {
                await _restore.ReplayActivePresetAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"PresetActivate.Replay failed: {ex.Message}");
            }
            ClearError();
            RenderPresetBar();
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _logic.OnSaved -= OnSaved;
            _logic.OnLoaded -= OnLoaded;
            if (_root is not null) _container.Remove(_root);
            _root = null;
        }

        // ---------- private ----------

        private void BuildBar()
        {
            if (_barTemplate is not null)
            {
                var clone = _barTemplate.CloneTree();
                _root = clone.Q<VisualElement>("vsb-preset-bar") ?? clone;
            }
            else
            {
                _root = new VisualElement { name = "vsb-preset-bar" };
                _root.AddToClassList("vsb-preset-bar");
                _activeLabel = new Label { name = ActiveLabelName };
                _root.Add(_activeLabel);
                _dropdown = new DropdownField { name = DropdownName };
                _root.Add(_dropdown);
                _nameInput = new TextField { name = NameInputName };
                _root.Add(_nameInput);
                _root.Add(new Button { name = CreateBtnName, text = "Create" });
                _root.Add(new Button { name = RenameBtnName, text = "Rename" });
                _root.Add(new Button { name = DuplicateBtnName, text = "Duplicate" });
                _root.Add(new Button { name = DeleteBtnName, text = "Delete" });
                _root.Add(new Button { name = ActivateBtnName, text = "Activate" });
                _errorLabel = new Label { name = ErrorLabelName };
                _root.Add(_errorLabel);
            }
            _container.Add(_root);
            _activeLabel ??= _root.Q<Label>(ActiveLabelName);
            _dropdown ??= _root.Q<DropdownField>(DropdownName);
            _nameInput ??= _root.Q<TextField>(NameInputName);
            _errorLabel ??= _root.Q<Label>(ErrorLabelName);

            // Wire button clicks. Buttons rely on UIElements panel routing; in
            // panel-less unit tests Presenter consumers call the *Async APIs
            // directly instead.
            var create = _root.Q<Button>(CreateBtnName);
            if (create is not null) create.clicked += async () =>
            {
                await CreatePresetAsync(_nameInput?.value ?? string.Empty);
                RenderPresetBar();
            };
            var rename = _root.Q<Button>(RenameBtnName);
            if (rename is not null) rename.clicked += async () =>
            {
                var id = ResolveSelectedPresetId();
                if (id is null) return;
                await RenamePresetAsync(id, _nameInput?.value ?? string.Empty);
                RenderPresetBar();
            };
            var dup = _root.Q<Button>(DuplicateBtnName);
            if (dup is not null) dup.clicked += async () =>
            {
                var id = ResolveSelectedPresetId();
                if (id is null) return;
                await DuplicatePresetAsync(id, _nameInput?.value ?? string.Empty);
                RenderPresetBar();
            };
            var del = _root.Q<Button>(DeleteBtnName);
            if (del is not null) del.clicked += async () =>
            {
                var id = ResolveSelectedPresetId();
                if (id is null) return;
                await DeletePresetAsync(id);
                RenderPresetBar();
            };
            var activate = _root.Q<Button>(ActivateBtnName);
            if (activate is not null) activate.clicked += async () =>
            {
                var id = ResolveSelectedPresetId();
                if (id is null) return;
                await ActivatePresetAsync(id);
            };
        }

        private string? ResolveSelectedPresetId()
        {
            if (_dropdown is null || _dropdown.value is null) return null;
            foreach (var h in _logic.ListPresets())
            {
                if (string.Equals(h.Name, _dropdown.value, StringComparison.Ordinal)) return h.PresetId;
            }
            return null;
        }

        private async Task<PresetOperationResult> HandleResultAsync(Task<PresetOperationResult> task)
        {
            var r = await task.ConfigureAwait(false);
            if (!r.Success) ShowError(r);
            else ClearError();
            RenderPresetBar();
            return r;
        }

        private void ShowError(PresetOperationResult r)
        {
            LastErrorMessage = $"{r.Error}: {r.Detail}";
            if (_errorLabel is not null) _errorLabel.text = LastErrorMessage;
            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"Preset operation failed: {LastErrorMessage}");
        }

        private void ClearError()
        {
            LastErrorMessage = null;
            if (_errorLabel is not null) _errorLabel.text = string.Empty;
        }

        private void OnSaved(PresetSavedEvent e) => RenderPresetBar();
        private void OnLoaded(PresetLoadEvent e) => RenderPresetBar();
    }
}
