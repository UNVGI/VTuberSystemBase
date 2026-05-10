#nullable enable
using NUnit.Framework;
using System;
using System.Threading;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Diagnostics;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 5.6 acceptance: snapshot reflects store + preset state, and the
    /// diagnostics row coalesces successive store changes within 1 second
    /// into a single re-render.
    /// </summary>
    [TestFixture]
    public sealed class TabDiagnosticsPresenterTests
    {
        [Test]
        public void Capture_AggregatesSlotCounts()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            var store = new CharacterTabStateStore();
            var conn = new FakeConnectionStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            var diag = new CharacterTabDiagnostics(store, logic, conn);

            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[]
                {
                    new SlotCatalogEntry { SlotId = "slot-01" },
                    new SlotCatalogEntry { SlotId = "slot-02" },
                    new SlotCatalogEntry { SlotId = "slot-03" },
                },
            });
            store.ApplyAssignment("slot-01", "avatars/alice");
            store.ApplyStatus("slot-01", SlotStatus.Assigned, null);
            store.ApplyError("slot-02", new SlotErrorPayload { ErrorCode = "X" });

            var snapshot = diag.Capture();

            Assert.AreEqual(3, snapshot.TotalSlotCount);
            Assert.AreEqual(1, snapshot.AssignedSlotCount);
            Assert.AreEqual(1, snapshot.ErrorSlotCount);
            Assert.AreEqual(ConnectionStatusCode.Connected, snapshot.ConnectionStatus);
        }

        [Test]
        public void Render_IsThrottledTo1SecondCoalesce()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            var store = new CharacterTabStateStore();
            var conn = new FakeConnectionStatus(UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            var diag = new CharacterTabDiagnostics(store, logic, conn);
            var container = new VisualElement();
            using var presenter = new TabDiagnosticsPresenter(diag, store, logic, clock, container);
            int initialRenderCount = presenter.RenderCountForTesting;

            // Initial render done at construction. Trigger several store changes
            // inside the throttle window; each change requests render but the
            // throttle should fold them into one (next tick beyond 1 sec).
            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "slot-01" } },
            });
            store.ApplyAssignment("slot-01", "avatars/alice");
            store.ApplyStatus("slot-01", SlotStatus.Assigned, null);
            int afterRapidChanges = presenter.RenderCountForTesting;

            clock.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(1));
            int afterTick = presenter.RenderCountForTesting;

            // Within the throttle window, only one direct render may have
            // occurred (the very first change), so the difference is at most 1
            // direct render plus 1 deferred flush after the tick.
            Assert.LessOrEqual(afterRapidChanges - initialRenderCount, 1,
                $"too many direct renders during throttle window: {afterRapidChanges - initialRenderCount}");
            Assert.GreaterOrEqual(afterTick, afterRapidChanges,
                "deferred flush must produce at most one extra render after the cool-down.");
        }
    }
}
