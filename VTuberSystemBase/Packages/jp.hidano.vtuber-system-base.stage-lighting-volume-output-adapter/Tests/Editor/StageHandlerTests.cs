#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class StageHandlerTests
    {
        private FakeOutputCommandDispatcher _dispatcher = null!;
        private FakeOutputSceneRoots _roots = null!;
        private FakeInstantiationProvider _provider = null!;
        private RecordingMessageSink _sink = null!;
        private AdapterLogger _logger = null!;
        private StageLightingVolumeOutputAdapterDiagnostics _diag = null!;
        private AdapterErrorReporter _reporter = null!;
        private StageCatalogBuilder _catalog = null!;
        private StageHandler _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new FakeOutputCommandDispatcher();
            _roots = new FakeOutputSceneRoots();
            _provider = new FakeInstantiationProvider();
            _sink = new RecordingMessageSink();
            _logger = new AdapterLogger();
            _diag = new StageLightingVolumeOutputAdapterDiagnostics();
            _reporter = new AdapterErrorReporter(_sink, _logger, _diag, () => 1);
            _catalog = new StageCatalogBuilder(_logger);
            _sut = new StageHandler(_dispatcher, _roots, _provider, _catalog, _reporter, _logger, _diag, _sink);
        }

        [TearDown]
        public void TearDown()
        {
            _sut.Dispose();
            _roots.Dispose();
            _dispatcher.Dispose();
        }

        [Test]
        public void Start_PublishesInitialStageCurrentNull_AndRegistersCommand()
        {
            _sut.Start();
            Assert.That(_dispatcher.HasEventHandler(StageLightingTopics.StageCommand), Is.True);
            // First state publish should be StageCurrent(null).
            Assert.That(_sink.PublishedStates.Any(p => p.Topic == StageLightingTopics.StageCurrent), Is.True);
            var first = _sink.PublishedStates.First(p => p.Topic == StageLightingTopics.StageCurrent);
            var dto = (StageCurrentDto)first.Payload!;
            Assert.That(dto.AddressableKey, Is.Null);
        }

        [Test]
        public void Load_Success_PublishesStageCurrentAndLoaded()
        {
            _provider.Configure("Stages/A");
            _sut.Start();
            _sink.Clear();

            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();

            // current state should reflect the new key.
            var states = _sink.PublishedStates.Where(p => p.Topic == StageLightingTopics.StageCurrent).ToList();
            Assert.That(states.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(((StageCurrentDto)states.Last().Payload!).AddressableKey, Is.EqualTo("Stages/A"));
            // event 'stage/loaded' should be published.
            var loadedEvents = _sink.PublishedEvents.Where(p => p.Topic == StageLightingTopics.StageLoaded).ToList();
            Assert.That(loadedEvents.Count, Is.EqualTo(1));
            Assert.That(_diag.CurrentStageAddressableKey, Is.EqualTo("Stages/A"));
            Assert.That(_sut.State.CurrentStage, Is.Not.Null);
        }

        [Test]
        public void Load_LazySwap_NewBeforeOldRelease()
        {
            _provider.Configure("Stages/A");
            _provider.Configure("Stages/B");
            _sut.Start();

            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();
            var oldStage = _sut.State.CurrentStage;
            Assert.That(oldStage, Is.Not.Null);
            Assert.That(_provider.ReleasedInstances.Count, Is.EqualTo(0));

            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/B")).GetAwaiter().GetResult();
            // After second load, old should have been released.
            Assert.That(_provider.ReleasedInstances.Count, Is.EqualTo(1));
            Assert.That(_provider.ReleasedInstances[0], Is.SameAs(oldStage));
            Assert.That(_diag.CurrentStageAddressableKey, Is.EqualTo("Stages/B"));
        }

        [Test]
        public void Load_Failure_KeepsOldStage_AndPublishesLoadFailed()
        {
            _provider.Configure("Stages/A");
            _provider.Configure("Stages/Bad", new FakeInstantiationProvider.KeyConfig
            {
                Success = false,
                ErrorCode = "load_failed",
                ErrorMessage = "boom",
            });
            _sut.Start();
            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();
            var keptStage = _sut.State.CurrentStage;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("stage_load_failed"));
            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/Bad")).GetAwaiter().GetResult();

            Assert.That(_sut.State.CurrentStage, Is.SameAs(keptStage));
            var failedEvents = _sink.PublishedEvents.Where(p => p.Topic == StageLightingTopics.StageLoadFailed).ToList();
            Assert.That(failedEvents.Count, Is.EqualTo(1));
            var dto = (StageLoadFailedDto)failedEvents[0].Payload!;
            Assert.That(dto.AddressableKey, Is.EqualTo("Stages/Bad"));
            Assert.That(dto.ErrorCode, Is.EqualTo("load_failed"));
        }

        [Test]
        public void Unload_ReleasesAndPublishesNullState()
        {
            _provider.Configure("Stages/A");
            _sut.Start();
            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();
            var stage = _sut.State.CurrentStage;
            _sink.Clear();

            _sut.HandleCommandAsync(new StageCommandDto("unload", null)).GetAwaiter().GetResult();
            Assert.That(_provider.ReleasedInstances, Contains.Item(stage));
            Assert.That(_sut.State.CurrentStage, Is.Null);
            var states = _sink.PublishedStates.Where(p => p.Topic == StageLightingTopics.StageCurrent).ToList();
            Assert.That(states.Count, Is.EqualTo(1));
            Assert.That(((StageCurrentDto)states[0].Payload!).AddressableKey, Is.Null);
        }

        [Test]
        public void Dispose_ReleasesCurrentStage()
        {
            _provider.Configure("Stages/A");
            _sut.Start();
            _sut.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();
            var stage = _sut.State.CurrentStage;
            _sut.Dispose();
            Assert.That(_provider.ReleasedInstances, Contains.Item(stage));
        }
    }
}
