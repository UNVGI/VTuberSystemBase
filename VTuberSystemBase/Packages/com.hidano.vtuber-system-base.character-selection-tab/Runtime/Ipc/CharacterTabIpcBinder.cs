#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Ipc
{
    public interface ICharacterTabIpcBinder : IDisposable
    {
        void SubscribeAll();
        void UnsubscribeAll();
        SendResult PublishAssignment(string slotId, string? avatarKey);
        SendResult PublishSettingValue(string slotId, string settingKey, SettingValue value);
        SendResult PublishSlotCommand(string slotId, SlotCommandPayload payload);
        Task<RequestResult<AvatarSettingsSchemaPayload>> RequestAvatarSchemaAsync(
            string avatarKey, TimeSpan timeout, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Production <see cref="ICharacterTabIpcBinder"/>. (task 3.1.)
    /// Subscribes to <c>slots/catalog</c>, <c>avatars/catalog</c>, and per-slot
    /// topics on demand. Outbound API is a thin Publish wrapper that records via
    /// diagnostics. Slot dynamic-subscription tokens are tracked so each removed
    /// slot's tokens can be disposed at <c>UnsubscribeAll</c> or when the slot
    /// disappears from the catalog.
    /// </summary>
    public sealed class CharacterTabIpcBinder : ICharacterTabIpcBinder
    {
        private readonly IUiCommandClient _cmd;
        private readonly IUiSubscriptionClient _sub;
        private readonly ICharacterTabStateStore _store;
        private readonly IDiagnosticsLogger? _log;

        private readonly List<ISubscriptionToken> _staticTokens = new List<ISubscriptionToken>();
        private readonly Dictionary<string, List<ISubscriptionToken>> _slotTokens =
            new Dictionary<string, List<ISubscriptionToken>>(StringComparer.Ordinal);
        private bool _subscribed;
        private bool _disposed;

        public CharacterTabIpcBinder(
            IUiCommandClient commandClient,
            IUiSubscriptionClient subscriptionClient,
            ICharacterTabStateStore store,
            IDiagnosticsLogger? logger = null)
        {
            _cmd = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _sub = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = logger;
            _store.OnChanged += OnStoreChanged;
        }

        public void SubscribeAll()
        {
            if (_subscribed || _disposed) return;
            _subscribed = true;

            _staticTokens.Add(_sub.Subscribe<SlotCatalogPayload>(
                CharacterTopics.SlotsCatalog, MessageKind.State, env =>
                {
                    if (env.Payload is null) return;
                    _store.ApplySlotCatalog(env.Payload);
                    SyncSlotSubscriptions();
                }));
            _staticTokens.Add(_sub.Subscribe<AvatarCatalogPayload>(
                CharacterTopics.AvatarsCatalog, MessageKind.State, env =>
                {
                    if (env.Payload is null) return;
                    _store.ApplyAvatarCatalog(env.Payload);
                }));

            // Dynamic per-slot subs follow once a SlotCatalog arrives. For any
            // slots already in the store (e.g. presenter pre-loaded), add now.
            SyncSlotSubscriptions();
        }

        public void UnsubscribeAll()
        {
            foreach (var tk in _staticTokens) tk.Dispose();
            _staticTokens.Clear();
            foreach (var kv in _slotTokens.ToArray())
            {
                foreach (var tk in kv.Value) tk.Dispose();
            }
            _slotTokens.Clear();
            _subscribed = false;
        }

        public SendResult PublishAssignment(string slotId, string? avatarKey)
        {
            var topic = CharacterTopics.SlotAssignment(slotId);
            var payload = new SlotAssignmentPayload { AvatarKey = avatarKey };
            var result = _cmd.PublishState(topic, payload);
            LogIpc("Ipc.SendSlotAssignment", topic, result);
            return result;
        }

        public SendResult PublishSettingValue(string slotId, string settingKey, SettingValue value)
        {
            var topic = CharacterTopics.SlotSettingValue(slotId, settingKey);
            var payload = new SlotSettingValuePayload
            {
                SettingKey = settingKey,
                Type = value.Type,
                Value = value.ToJson(),
            };
            var result = _cmd.PublishState(topic, payload);
            LogIpc("Ipc.SendSlotSettings", topic, result);
            return result;
        }

        public SendResult PublishSlotCommand(string slotId, SlotCommandPayload payload)
        {
            var topic = CharacterTopics.SlotCommand(slotId);
            var result = _cmd.PublishEvent(topic, payload);
            LogIpc("Ipc.SendSlotCommand", topic, result);
            return result;
        }

        public Task<RequestResult<AvatarSettingsSchemaPayload>> RequestAvatarSchemaAsync(
            string avatarKey, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var topic = CharacterTopics.AvatarSchema(avatarKey);
            var req = new AvatarSchemaRequestPayload { AvatarKey = avatarKey };
            return _cmd.RequestAsync<AvatarSchemaRequestPayload, AvatarSettingsSchemaPayload>(
                topic, req, timeout, cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            UnsubscribeAll();
        }

        // ---------- private ----------

        private void OnStoreChanged(StateChangeScope scope)
        {
            if ((scope & StateChangeScope.SlotCatalog) != 0)
            {
                SyncSlotSubscriptions();
            }
        }

        private void SyncSlotSubscriptions()
        {
            var slots = _store.ListSlots();
            var keep = new HashSet<string>(slots.Select(s => s.SlotId), StringComparer.Ordinal);

            // Add subs for new slots.
            foreach (var slot in slots)
            {
                if (_slotTokens.ContainsKey(slot.SlotId)) continue;
                _slotTokens[slot.SlotId] = AddSlotSubs(slot.SlotId);
            }
            // Remove subs for retired slots.
            foreach (var existing in _slotTokens.Keys.ToArray())
            {
                if (keep.Contains(existing)) continue;
                foreach (var tk in _slotTokens[existing]) tk.Dispose();
                _slotTokens.Remove(existing);
            }
        }

        private List<ISubscriptionToken> AddSlotSubs(string slotId)
        {
            var tokens = new List<ISubscriptionToken>(4);
            tokens.Add(_sub.Subscribe<SlotAssignmentPayload>(
                CharacterTopics.SlotAssignment(slotId), MessageKind.State, env =>
                {
                    if (env.Payload is null) return;
                    _store.ApplyAssignment(slotId, env.Payload.AvatarKey);
                }));
            tokens.Add(_sub.Subscribe<SlotStatusPayload>(
                CharacterTopics.SlotStatus(slotId), MessageKind.State, env =>
                {
                    if (env.Payload is null) return;
                    var s = ParseStatus(env.Payload.Status);
                    _store.ApplyStatus(slotId, s, env.Payload.Detail);
                }));
            tokens.Add(_sub.Subscribe<SlotErrorPayload>(
                CharacterTopics.SlotError(slotId), MessageKind.Event, env =>
                {
                    if (env.Payload is null) return;
                    _store.ApplyError(slotId, env.Payload);
                    _log?.Log(LogLevel.Warning, LogCategory.Ipc,
                        $"Ipc.Receive slot/{slotId}/error code={env.Payload.ErrorCode}",
                        new { slotId, errorCode = env.Payload.ErrorCode });
                }));
            return tokens;
        }

        private static SlotStatus ParseStatus(string raw) => raw switch
        {
            "Empty" => SlotStatus.Empty,
            "Assigning" => SlotStatus.Assigning,
            "Assigned" => SlotStatus.Assigned,
            "Error" => SlotStatus.Error,
            _ => SlotStatus.Empty,
        };

        private void LogIpc(string code, string topic, SendResult result)
        {
            if (result.Success) return;
            _log?.Log(LogLevel.Warning, LogCategory.Ipc,
                $"{code} failed topic={topic} error={result.Error?.Code}",
                new { code, topic, error = result.Error?.Code.ToString() });
        }
    }
}
