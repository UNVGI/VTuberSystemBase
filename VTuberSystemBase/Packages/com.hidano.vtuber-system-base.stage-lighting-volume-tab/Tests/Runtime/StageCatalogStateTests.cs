#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the <c>stage/catalog</c> subscription semantics implemented by
    /// <see cref="StageCatalogState"/> (Task 3.3, Requirements 3.1, 3.9, 3.10).
    /// </summary>
    [TestFixture]
    public sealed class StageCatalogStateTests
    {
        [Test]
        public void StartSubscribing_RegistersStageCatalogSubscription()
        {
            var ipc = new FakeIpcClient();
            var sut = new StageCatalogState(ipc, new FakeDiagnosticsLogger());

            sut.StartSubscribing();

            Assert.That(ipc.Subscriptions, Has.Count.EqualTo(1));
            Assert.That(ipc.Subscriptions[0].Topic, Is.EqualTo(StageLightingTopics.StageCatalog));
            Assert.That(ipc.Subscriptions[0].Kind, Is.EqualTo(MessageKind.State));
        }

        [Test]
        public void IncomingCatalog_UpdatesCurrentEntries_AndRaisesChanged()
        {
            var ipc = new FakeIpcClient();
            var sut = new StageCatalogState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();

            int changes = 0;
            sut.Changed += () => changes++;

            ipc.Emit(StageLightingTopics.StageCatalog, new StageCatalogDto(new List<StageCatalogEntryDto>
            {
                new StageCatalogEntryDto("stages/a", "Stage A", null),
                new StageCatalogEntryDto("stages/b", "Stage B", "thumbs/b"),
            }));

            Assert.That(changes, Is.EqualTo(1));
            Assert.That(sut.Entries, Has.Count.EqualTo(2));
            Assert.That(sut.Entries[0].DisplayName, Is.EqualTo("Stage A"));
            Assert.That(sut.IsLoaded, Is.True);
        }

        [Test]
        public void TryFind_ReturnsEntry_WhenAddressableKeyMatches()
        {
            var ipc = new FakeIpcClient();
            var sut = new StageCatalogState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();
            ipc.Emit(StageLightingTopics.StageCatalog, new StageCatalogDto(new List<StageCatalogEntryDto>
            {
                new StageCatalogEntryDto("stages/a", "Stage A", null),
            }));

            var ok = sut.TryFind("stages/a", out var entry);
            Assert.That(ok, Is.True);
            Assert.That(entry.DisplayName, Is.EqualTo("Stage A"));

            var notFound = sut.TryFind("stages/missing", out _);
            Assert.That(notFound, Is.False);
        }

        [Test]
        public void Dispose_DropsSubscription()
        {
            var ipc = new FakeIpcClient();
            var sut = new StageCatalogState(ipc, new FakeDiagnosticsLogger());
            sut.StartSubscribing();

            sut.Dispose();

            Assert.That(ipc.Subscriptions, Is.Empty);
        }
    }
}
