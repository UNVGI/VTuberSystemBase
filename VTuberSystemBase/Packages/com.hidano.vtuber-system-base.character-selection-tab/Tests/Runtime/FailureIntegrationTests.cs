#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using ShellMessage = VTuberSystemBase.UiToolkitShell.Commands.MessageKind;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Tasks 7.1 / 7.2: cross-component degradation. Validates that
    /// slot/{id}/error events flip the slot card to the error visual without
    /// affecting other slots, and that disconnect → reconnect drives the UI
    /// through degraded → recovered states with the orchestrator re-replaying.
    /// </summary>
    [TestFixture]
    public sealed class FailureIntegrationTests
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

        [Test]
        public void RacError_FlipsOnlyTargetSlotIntoErrorState()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();
            using var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root);

            // Inject a 2-slot catalog from main output side.
            sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[]
                {
                    new SlotCatalogEntry { SlotId = "slot-01" },
                    new SlotCatalogEntry { SlotId = "slot-02" },
                },
            });

            // Slot-01 receives an error event.
            sub.Emit(CharacterTopics.SlotError("slot-01"),
                new SlotErrorPayload { ErrorCode = "ApplyFailed", Detail = "rig" },
                ShellMessage.Event);

            var slot1 = boot.StoreForTesting.GetSlot("slot-01");
            var slot2 = boot.StoreForTesting.GetSlot("slot-02");
            Assert.AreEqual(SlotStatus.Error, slot1!.Status);
            Assert.AreEqual(SlotStatus.Empty, slot2!.Status);

            // Verify the diagnostics ledger captured the failure (Req 9.4).
            bool sawError = false;
            foreach (var e in logger.Entries)
            {
                if (e.Category == LogCategory.Ipc && e.Message.Contains("slot/slot-01/error"))
                {
                    sawError = true;
                    break;
                }
            }
            Assert.IsTrue(sawError);
        }

        [Test]
        public void DisconnectReconnect_ReplaysActivePreset()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus(UiToolkitShell.Commands.ConnectionStatusCode.Disconnected);
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();
            using var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root);

            // Seed an active preset with one assignment.
            var presets = boot.PresetsForTesting;
            // InitializeAsync is fired-and-forget by the bootstrapper; complete it here for determinism.
            presets.InitializeAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            var created = presets.CreateAsync("Morning").GetAwaiter().GetResult();
            Assert.IsTrue(created.Success);
            presets.SetActiveAsync(created.PresetId!).GetAwaiter().GetResult();
            presets.MarkSlotAssignmentChanged("slot-01", "avatars/alice");
            presets.FlushPendingAsync().GetAwaiter().GetResult();

            // First connection establishes; orchestrator replays once.
            int sentBefore = cmd.Sent.Count;
            conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            int sentAfterConnect = cmd.Sent.Count;
            Assert.Greater(sentAfterConnect, sentBefore,
                "first connect must trigger ReplayActivePresetAsync.");

            // Disconnect / reconnect should re-replay.
            conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Disconnected);
            int sentAfterDisconnect = cmd.Sent.Count;
            conn.SetStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            int sentAfterReconnect = cmd.Sent.Count;
            Assert.Greater(sentAfterReconnect, sentAfterDisconnect,
                "reconnect must re-trigger replay.");
        }
    }
}
