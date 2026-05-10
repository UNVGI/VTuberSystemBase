#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;
using ShellMessage = VTuberSystemBase.UiToolkitShell.Commands.MessageKind;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.PlayMode
{
    /// <summary>
    /// PlayMode sample driver. (task 8.2.) Attach to an empty GameObject in
    /// <c>CharacterTabPlayModeSample.unity</c> together with a UIDocument; the
    /// driver constructs the bootstrapper with mock IPC + asset doubles and
    /// emits a deterministic catalog so an operator can manually exercise the
    /// click flows. The sample scene must be created in-editor — see README
    /// "PlayMode サンプル" section for the procedure.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterTabPlayModeSampleDriver : MonoBehaviour
    {
        [SerializeField] private VisualTreeAsset? _tabUxml;
        [SerializeField] private List<StyleSheet> _styleSheets = new List<StyleSheet>();

        private FakeTabLifecycleHandle? _handle;
        private FakeUiCommandClient? _cmd;
        private FakeUiSubscriptionClient? _sub;
        private FakeConnectionStatus? _conn;
        private FakeAsyncAssetLoader? _loader;
        private FakeDiagnosticsLogger? _logger;
        private InMemoryPresetStorage? _storage;
        private ManualClock? _clock;
        private CharacterTabBootstrapper? _boot;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (_tabUxml is not null) doc.visualTreeAsset = _tabUxml;
            foreach (var sheet in _styleSheets)
            {
                if (sheet is null) continue;
                if (!doc.rootVisualElement.styleSheets.Contains(sheet))
                {
                    doc.rootVisualElement.styleSheets.Add(sheet);
                }
            }

            var root = doc.rootVisualElement.Q<VisualElement>(ViewQueryHelpers.TabRootName)
                       ?? doc.rootVisualElement;
            _handle = new FakeTabLifecycleHandle();
            _cmd = new FakeUiCommandClient
            {
                RequestResponder = _ => BuildSchema("avatars/alice"),
            };
            _sub = new FakeUiSubscriptionClient();
            _conn = new FakeConnectionStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            _loader = new FakeAsyncAssetLoader();
            _logger = new FakeDiagnosticsLogger();
            _storage = new InMemoryPresetStorage();
            _clock = new ManualClock();

            _boot = new CharacterTabBootstrapper(
                _handle, _cmd, _sub, _conn, _loader, _logger,
                _storage, _clock, root);

            // Push a deterministic catalog + avatar candidate so the manual
            // operator immediately sees player cards and an avatar tile.
            _sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[]
                {
                    new SlotCatalogEntry { SlotId = "slot-01", DisplayName = "Player 1" },
                    new SlotCatalogEntry { SlotId = "slot-02", DisplayName = "Player 2" },
                    new SlotCatalogEntry { SlotId = "slot-03", DisplayName = "Player 3" },
                },
            });
            _sub.Emit(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload
            {
                Avatars = new[]
                {
                    new AvatarCatalogEntry { AvatarKey = "avatars/alice", DisplayName = "Alice" },
                    new AvatarCatalogEntry { AvatarKey = "avatars/bob", DisplayName = "Bob" },
                    new AvatarCatalogEntry { AvatarKey = "avatars/carol", DisplayName = "Carol" },
                },
            });
            _handle.FireActivated();
        }

        private void Update()
        {
            // Drive the manual clock once per frame so debounce / interaction
            // idle paths progress in real time during PlayMode.
            _clock?.SetUtcNow(System.DateTimeOffset.UtcNow);
        }

        private void OnDisable()
        {
            _boot?.Dispose();
            _boot = null;
        }

        private static AvatarSettingsSchemaPayload BuildSchema(string avatarKey)
        {
            JsonElement Json(float v)
            {
                using var doc = JsonDocument.Parse(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return doc.RootElement.Clone();
            }
            return new AvatarSettingsSchemaPayload
            {
                AvatarKey = avatarKey,
                Settings = new[]
                {
                    new Contracts.SettingSchemaEntry
                    {
                        Key = "expression.smile",
                        Label = "Smile",
                        Type = SettingType.Float,
                        Default = Json(0.5f),
                        Min = Json(0f),
                        Max = Json(1f),
                    },
                    new Contracts.SettingSchemaEntry
                    {
                        Key = "body.scale",
                        Label = "Body Scale",
                        Type = SettingType.Vector3,
                        Default = default,
                        Min = Json(0f),
                        Max = Json(2f),
                    },
                },
            };
        }
    }
}
