#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using SchemaEntryDto = VTuberSystemBase.CharacterSelectionTab.Contracts.SettingSchemaEntry;
using SchemaEntry = VTuberSystemBase.CharacterSelectionTab.State.SettingSchemaEntry;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    /// <summary>
    /// Hosts the per-slot settings panel. (task 5.4.) Fetches the avatar
    /// schema via <see cref="ICharacterTabIpcBinder.RequestAvatarSchemaAsync"/>,
    /// builds dynamic controls through <see cref="IDynamicSettingControlFactory"/>,
    /// publishes <c>PublishState</c> on continuous edits and <c>PublishEvent</c>
    /// on discrete <c>command</c> entries. Active slot avatar changes trigger
    /// a Close + OpenForAsync rebuild (Req 5.10).
    /// </summary>
    public sealed class SettingsPanelPresenter : IDisposable
    {
        public const string PanelHostName = "vsb-char-tab__settings-panel";
        public const string ErrorMessageName = "vsb-char-tab__settings-panel__error";

        private readonly ICharacterTabStateStore _store;
        private readonly ICharacterTabIpcBinder _binder;
        private readonly IDynamicSettingControlFactory _factory;
        private readonly IInteractionGuard _guard;
        private readonly VisualElement _container;
        private readonly TimeSpan _schemaTimeout;
        private readonly IDiagnosticsLogger? _log;

        private string? _activeSlot;
        private string? _activeAvatar;
        private readonly List<SettingControl> _controls = new List<SettingControl>();
        private readonly Dictionary<string, SchemaEntry> _resolvedSchema =
            new Dictionary<string, SchemaEntry>(StringComparer.Ordinal);
        private bool _disposed;

        public SettingsPanelPresenter(
            ICharacterTabStateStore store,
            ICharacterTabIpcBinder binder,
            IDynamicSettingControlFactory factory,
            IInteractionGuard guard,
            VisualElement container,
            TimeSpan schemaTimeout,
            IDiagnosticsLogger? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            if (schemaTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(schemaTimeout));
            _schemaTimeout = schemaTimeout;
            _log = logger;

            _store.OnChanged += OnStoreChanged;
            _guard.OnChanged += OnGuardChanged;
        }

        public string? ActiveSlot => _activeSlot;
        public IReadOnlyList<SettingControl> ControlsForTesting => _controls;

        public async Task OpenForAsync(string slotId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(slotId)) throw new ArgumentException("slotId required", nameof(slotId));
            var slot = _store.GetSlot(slotId);
            if (slot is null)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"SettingsPanel.OpenForAsync: unknown slot '{slotId}'.");
                return;
            }
            if (string.IsNullOrEmpty(slot.AssignedAvatarKey))
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"SettingsPanel.OpenForAsync: slot '{slotId}' has no avatar assigned.");
                return;
            }

            Close();
            _activeSlot = slotId;
            _activeAvatar = slot.AssignedAvatarKey;

            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"SettingSchema.RequestStart slot={slotId} avatar={slot.AssignedAvatarKey}");
            var result = await _binder.RequestAvatarSchemaAsync(
                slot.AssignedAvatarKey!, _schemaTimeout, cancellationToken).ConfigureAwait(false);
            if (_disposed || !string.Equals(_activeSlot, slotId, StringComparison.Ordinal)) return;

            if (!result.Success || result.Response is null)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"SettingSchema.RequestFailed slot={slotId} avatar={slot.AssignedAvatarKey} error={result.Error?.Code}");
                ShowError($"Failed to load settings schema: {result.Error?.Code}");
                return;
            }
            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"SettingSchema.RequestComplete slot={slotId} entries={result.Response.Settings.Count}");

            BuildControls(slotId, result.Response);
        }

        public void Close()
        {
            foreach (var c in _controls)
            {
                if (c.Root is not null) _container.Remove(c.Root);
            }
            _controls.Clear();
            _resolvedSchema.Clear();
            _activeSlot = null;
            _activeAvatar = null;
            ClearMessages();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            _guard.OnChanged -= OnGuardChanged;
            Close();
        }

        // ---------- private ----------

        private void BuildControls(string slotId, AvatarSettingsSchemaPayload schema)
        {
            ClearMessages();
            var slot = _store.GetSlot(slotId);
            if (slot is null) return;
            foreach (var entry in schema.Settings)
            {
                if (entry is null) continue;
                if (string.IsNullOrEmpty(entry.Key))
                {
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        "SettingsPanel: schema entry with empty key, skipping.");
                    continue;
                }
                var resolved = ResolveSchema(entry);
                _resolvedSchema[entry.Key] = resolved;
                slot.SettingValues.TryGetValue(entry.Key, out var initial);
                if (initial.Equals(default(SettingValue)) && resolved.Default.HasValue)
                {
                    initial = resolved.Default.Value;
                }
                var control = _factory.Build(resolved, initial);
                if (control.Root is null)
                {
                    // Skipped due to schema problems; factory has already logged.
                    continue;
                }
                _container.Add(control.Root);
                _controls.Add(control);
                WireControl(slotId, entry.Key, resolved, control);
            }
        }

        private void WireControl(string slotId, string settingKey, SchemaEntry resolved, SettingControl control)
        {
            // Continuous values → state; command kind → event.
            if (control.IsCommand)
            {
                control.CommandTriggeredEvent += () =>
                {
                    var payload = new SlotCommandPayload { Kind = "PresetApply", Argument = settingKey };
                    var result = _binder.PublishSlotCommand(slotId, payload);
                    if (!result.Success)
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"Setting.Command failed slot={slotId} key={settingKey} error={result.Error?.Code}");
                    }
                };
                return;
            }
            control.ValueChangedEvent += value =>
            {
                if (!ValidateRange(resolved, value))
                {
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        $"Setting.Change suppressed: out-of-range slot={slotId} key={settingKey}");
                    return;
                }
                _store.MarkInteracting(slotId, settingKey);
                _guard.MarkInteracting(slotId, settingKey);
                _store.ApplySettingValue(slotId, settingKey, value, isFromRemote: false);
                var result = _binder.PublishSettingValue(slotId, settingKey, value);
                if (!result.Success)
                {
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        $"Setting.Change publish failed slot={slotId} key={settingKey} error={result.Error?.Code}");
                }
            };
            control.InteractingChangedEvent += interacting =>
            {
                if (interacting)
                {
                    _store.MarkInteracting(slotId, settingKey);
                    _guard.MarkInteracting(slotId, settingKey);
                }
                else
                {
                    _guard.EndInteracting(slotId, settingKey);
                    // Store flush handled by InteractionGuard.OnChanged → OnGuardChanged.
                }
            };
        }

        private void OnStoreChanged(StateChangeScope scope)
        {
            if ((scope & StateChangeScope.Assignment) == 0) return;
            if (_activeSlot is null) return;
            var slot = _store.GetSlot(_activeSlot);
            if (slot is null) { Close(); return; }
            // Avatar swap on the active slot triggers a rebuild.
            if (!string.Equals(slot.AssignedAvatarKey, _activeAvatar, StringComparison.Ordinal))
            {
                var slotIdToRebuild = _activeSlot;
                Close();
                if (slot.AssignedAvatarKey is not null)
                {
                    _ = OpenForAsync(slotIdToRebuild!, CancellationToken.None);
                }
            }
        }

        private void OnGuardChanged(InteractingChangedEventArgs e)
        {
            if (e.IsInteracting) return;
            // Idle: flush buffered remote state into the store.
            _store.FlushBufferedSetting(e.SlotId, e.SettingKey);
        }

        private void ShowError(string message)
        {
            ClearMessages();
            var error = new VisualElement { name = ErrorMessageName };
            error.Add(new Label(message));
            error.Add(new Button(() =>
            {
                if (_activeSlot is not null) _ = OpenForAsync(_activeSlot, CancellationToken.None);
            })
            { text = "Retry" });
            _container.Add(error);
        }

        private void ClearMessages()
        {
            var existing = _container.Q<VisualElement>(ErrorMessageName);
            if (existing is not null) _container.Remove(existing);
        }

        private static SchemaEntry ResolveSchema(SchemaEntryDto dto)
        {
            return new SchemaEntry
            {
                Key = dto.Key,
                Label = dto.Label,
                Type = dto.Type,
                Default = dto.Default.HasValue ? SettingValue.FromJson(dto.Type, dto.Default.Value) : (SettingValue?)null,
                Min = dto.Min.HasValue ? SettingValue.FromJson(dto.Type, dto.Min.Value) : (SettingValue?)null,
                Max = dto.Max.HasValue ? SettingValue.FromJson(dto.Type, dto.Max.Value) : (SettingValue?)null,
                Unit = dto.Unit,
                Options = dto.Options,
                Kind = dto.Kind,
                Step = dto.Step,
            };
        }

        private static bool ValidateRange(SchemaEntry resolved, SettingValue value)
        {
            switch (resolved.Type)
            {
                case SettingType.Float:
                    if (resolved.Min is { } mn && value.FloatValue < mn.FloatValue) return false;
                    if (resolved.Max is { } mx && value.FloatValue > mx.FloatValue) return false;
                    return true;
                case SettingType.Int:
                    if (resolved.Min is { } imn && value.IntValue < imn.IntValue) return false;
                    if (resolved.Max is { } imx && value.IntValue > imx.IntValue) return false;
                    return true;
                default:
                    return true;
            }
        }
    }
}
