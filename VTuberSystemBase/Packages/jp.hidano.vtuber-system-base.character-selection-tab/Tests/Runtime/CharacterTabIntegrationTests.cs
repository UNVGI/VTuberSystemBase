#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;
using ShellMessage = VTuberSystemBase.UiToolkitShell.Commands.MessageKind;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 8.1: end-to-end integration scenarios driven through the
    /// composition root with all real components (state / services /
    /// presenters) wired together and only the IPC + asset boundaries faked.
    /// Covers initial sync, assignment round-trip, settings schema, preset
    /// activation, and disconnect / reconnect.
    /// </summary>
    [TestFixture]
    public sealed class CharacterTabIntegrationTests
    {
        private static VisualElement BuildRoot()
        {
            var root = new VisualElement { name = ViewQueryHelpers.TabRootName };
            root.Add(new VisualElement { name = ViewQueryHelpers.PresetBarRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.PlayerCardsRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.AvatarCatalogRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.SettingsPanelRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.DiagnosticsRegion });
            return root;
        }

        private sealed class IntegrationFixture : IDisposable
        {
            public FakeTabLifecycleHandle Handle { get; } = new FakeTabLifecycleHandle();
            public FakeUiCommandClient Cmd { get; } = new FakeUiCommandClient();
            public FakeUiSubscriptionClient Sub { get; } = new FakeUiSubscriptionClient();
            public FakeConnectionStatus Conn { get; }
            public FakeAsyncAssetLoader Loader { get; } = new FakeAsyncAssetLoader();
            public FakeDiagnosticsLogger Logger { get; } = new FakeDiagnosticsLogger();
            public InMemoryPresetStorage Storage { get; } = new InMemoryPresetStorage();
            public ManualClock Clock { get; } = new ManualClock();
            public VisualElement Root { get; } = BuildRoot();
            public CharacterTabBootstrapper Boot { get; }

            public IntegrationFixture(UiToolkitShell.Commands.ConnectionStatusCode initial =
                UiToolkitShell.Commands.ConnectionStatusCode.Disconnected)
            {
                Conn = new FakeConnectionStatus(initial);
                Boot = new CharacterTabBootstrapper(
                    Handle, Cmd, Sub, Conn, Loader, Logger, Storage, Clock, Root);
            }

            public void Dispose() => Boot.Dispose();
        }

        [Test]
        public void Scenario1_InitialCatalogSync_ReachesSlotListPresenter()
        {
            using var fx = new IntegrationFixture(UiToolkitShell.Commands.ConnectionStatusCode.Connected);

            fx.Sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[]
                {
                    new SlotCatalogEntry { SlotId = "slot-01" },
                    new SlotCatalogEntry { SlotId = "slot-02" },
                },
            });
            fx.Sub.Emit(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload
            {
                Avatars = new[] { new Contracts.AvatarCatalogEntry { AvatarKey = "avatars/alice", DisplayName = "Alice" } },
            });

            var slots = fx.Boot.StoreForTesting.ListSlots();
            Assert.AreEqual(2, slots.Count);
            Assert.AreEqual(1, fx.Boot.StoreForTesting.AvatarCatalog.Count);
        }

        [Test]
        public void Scenario2_AssignmentRoundTrip_TransitionsToAssigned()
        {
            using var fx = new IntegrationFixture(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            fx.Sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "slot-01" } },
            });
            fx.Sub.Emit(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload
            {
                Avatars = new[] { new Contracts.AvatarCatalogEntry { AvatarKey = "avatars/alice" } },
            });

            // Drive assignment via the public Binder API.
            var sendResult = fx.Boot.BinderForTesting.PublishAssignment("slot-01", "avatars/alice");
            Assert.IsTrue(sendResult.Success);
            // Echo back the assignment + status.
            fx.Sub.Emit(CharacterTopics.SlotAssignment("slot-01"),
                new SlotAssignmentPayload { AvatarKey = "avatars/alice" });
            fx.Sub.Emit(CharacterTopics.SlotStatus("slot-01"),
                new SlotStatusPayload { Status = "Assigned" });

            var slot = fx.Boot.StoreForTesting.GetSlot("slot-01");
            Assert.AreEqual(SlotStatus.Assigned, slot!.Status);
            Assert.AreEqual("avatars/alice", slot.AssignedAvatarKey);
        }

        [Test]
        public async Task Scenario3_SchemaRequest_BuildsDynamicControls()
        {
            using var fx = new IntegrationFixture(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            fx.Cmd.RequestResponder = _ =>
            {
                JsonElement Json(float v)
                {
                    using var doc = JsonDocument.Parse(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return doc.RootElement.Clone();
                }
                return new AvatarSettingsSchemaPayload
                {
                    AvatarKey = "avatars/alice",
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
                    },
                };
            };
            fx.Sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "slot-01" } },
            });
            fx.Sub.Emit(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload
            {
                Avatars = new[] { new Contracts.AvatarCatalogEntry { AvatarKey = "avatars/alice" } },
            });
            fx.Sub.Emit(CharacterTopics.SlotAssignment("slot-01"),
                new SlotAssignmentPayload { AvatarKey = "avatars/alice" });

            // Use the binder request directly to assert schema shape; the
            // SettingsPanelPresenter owns the build-and-mount path covered by
            // its dedicated tests.
            var resp = await fx.Boot.BinderForTesting.RequestAvatarSchemaAsync(
                "avatars/alice", TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.IsTrue(resp.Success);
            Assert.AreEqual(1, resp.Response!.Settings.Count);
        }

        [Test]
        public async Task Scenario4_PresetActivation_ReplaysAssignmentsViaState()
        {
            using var fx = new IntegrationFixture(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            fx.Sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "slot-01" } },
            });
            fx.Sub.Emit(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload
            {
                Avatars = new[] { new Contracts.AvatarCatalogEntry { AvatarKey = "avatars/alice" } },
            });
            await fx.Boot.PresetsForTesting.InitializeAsync(CancellationToken.None);
            var created = await fx.Boot.PresetsForTesting.CreateAsync("Morning");
            Assert.IsTrue(created.Success);
            await fx.Boot.PresetsForTesting.SetActiveAsync(created.PresetId!);
            fx.Boot.PresetsForTesting.MarkSlotAssignmentChanged("slot-01", "avatars/alice");
            await fx.Boot.PresetsForTesting.FlushPendingAsync();

            int sentBefore = fx.Cmd.Sent.Count;

            // Disconnect → reconnect to trigger orchestrator replay.
            fx.Conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Disconnected);
            fx.Conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);

            int assignmentSends = 0;
            for (int i = sentBefore; i < fx.Cmd.Sent.Count; i++)
            {
                if (fx.Cmd.Sent[i].Topic == CharacterTopics.SlotAssignment("slot-01")) assignmentSends++;
            }
            Assert.GreaterOrEqual(assignmentSends, 1, "preset activation must replay the slot's assignment.");
        }

        [Test]
        public void Scenario5_DisconnectReconnect_DoesNotCorruptStore()
        {
            using var fx = new IntegrationFixture();
            fx.Sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "slot-01" } },
            });

            fx.Conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            fx.Conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Disconnected);
            fx.Conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);

            // Store still has the slot; subscriptions remained alive.
            Assert.IsNotNull(fx.Boot.StoreForTesting.GetSlot("slot-01"));
        }
    }
}
