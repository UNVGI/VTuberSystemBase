#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the <c>lights/list</c> subscription and diff semantics implemented by
    /// <see cref="LightListState"/> (Task 3.3, Requirements 4.1, 4.4, 4.8, 7.8).
    /// </summary>
    [TestFixture]
    public sealed class LightListStateTests
    {
        [Test]
        public void StartSubscribing_RegistersLightsListSubscription()
        {
            var ipc = new FakeIpcClient();
            var logger = new FakeDiagnosticsLogger();
            var sut = new LightListState(ipc, logger);

            sut.StartSubscribing();

            Assert.That(ipc.Subscriptions, Has.Count.EqualTo(1));
            Assert.That(ipc.Subscriptions[0].Topic, Is.EqualTo(StageLightingTopics.LightsList));
            Assert.That(ipc.Subscriptions[0].Kind, Is.EqualTo(MessageKind.State));
        }

        [Test]
        public void OnInitialList_PublishesAddedDiff()
        {
            var ipc = new FakeIpcClient();
            var sut = new LightListState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();

            LightListChangeEvent? captured = null;
            sut.Changed += ev => captured = ev;

            ipc.Emit(StageLightingTopics.LightsList, new LightListDto(new List<LightListItemDto>
            {
                new LightListItemDto("a", "Sun", LightTypeDto.Directional),
                new LightListItemDto("b", "Fill", LightTypeDto.Point),
            }));

            Assert.That(captured.HasValue, Is.True);
            Assert.That(captured!.Value.Added, Has.Count.EqualTo(2));
            Assert.That(captured.Value.Removed, Is.Empty);
            Assert.That(sut.CurrentList, Has.Count.EqualTo(2));
            Assert.That(sut.CurrentList[0].LightId, Is.EqualTo("a"));
            Assert.That(sut.CurrentList[1].LightId, Is.EqualTo("b"));
        }

        [Test]
        public void OnUpdatedList_DiffsAdditionsAndRemovalsAndKeepsStableOrder()
        {
            var ipc = new FakeIpcClient();
            var sut = new LightListState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();

            ipc.Emit(StageLightingTopics.LightsList, new LightListDto(new List<LightListItemDto>
            {
                new LightListItemDto("a", "Sun", LightTypeDto.Directional),
                new LightListItemDto("b", "Fill", LightTypeDto.Point),
            }));

            var captured = new List<LightListChangeEvent>();
            sut.Changed += ev => captured.Add(ev);

            // Remove "a", add "c"
            ipc.Emit(StageLightingTopics.LightsList, new LightListDto(new List<LightListItemDto>
            {
                new LightListItemDto("b", "Fill", LightTypeDto.Point),
                new LightListItemDto("c", "Rim", LightTypeDto.Spot),
            }));

            Assert.That(captured, Has.Count.EqualTo(1));
            Assert.That(captured[0].Added, Has.Count.EqualTo(1));
            Assert.That(captured[0].Added[0].LightId, Is.EqualTo("c"));
            Assert.That(captured[0].Removed, Has.Count.EqualTo(1));
            Assert.That(captured[0].Removed[0], Is.EqualTo("a"));
            // Stable order: existing lights keep their slots, new ones append at end.
            Assert.That(sut.CurrentList[0].LightId, Is.EqualTo("b"));
            Assert.That(sut.CurrentList[1].LightId, Is.EqualTo("c"));
        }

        [Test]
        public void DuplicateLightIdInList_LogsWarning_AndKeepsFirstOccurrence()
        {
            var ipc = new FakeIpcClient();
            var logger = new FakeDiagnosticsLogger();
            var sut = new LightListState(ipc, logger);
            sut.StartSubscribing();

            ipc.Emit(StageLightingTopics.LightsList, new LightListDto(new List<LightListItemDto>
            {
                new LightListItemDto("dup", "First", LightTypeDto.Directional),
                new LightListItemDto("dup", "Second", LightTypeDto.Spot),
            }));

            Assert.That(sut.CurrentList, Has.Count.EqualTo(1));
            Assert.That(sut.CurrentList[0].DisplayName, Is.EqualTo("First"));
            bool warned = false;
            foreach (var entry in logger.Entries)
            {
                if (entry.Level >= LogLevel.Warning) warned = true;
            }
            Assert.That(warned, Is.True);
        }

        [Test]
        public void StopSubscribing_DropsSubscription()
        {
            var ipc = new FakeIpcClient();
            var sut = new LightListState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();
            sut.StopSubscribing();

            Assert.That(ipc.Subscriptions, Is.Empty);
        }

        [Test]
        public void Dispose_DropsSubscription()
        {
            var ipc = new FakeIpcClient();
            var sut = new LightListState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();

            sut.Dispose();

            Assert.That(ipc.Subscriptions, Is.Empty);
        }
    }
}
