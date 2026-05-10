#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// Self-loop IPC integration test (Task 8.1, Requirements 12.2, 12.5). Uses
    /// <see cref="FakeIpcClient"/> as the in-memory loopback so the entire IPC
    /// contract round-trips even before a real main-output adapter exists. Each
    /// assertion exercises one of the contractual flows from the design.
    /// </summary>
    [TestFixture]
    public sealed class SelfLoopIpcTest
    {
        private static (StageLightingVolumeTabViewModel vm,
                        FakeIpcClient ipc,
                        FakeClock clock,
                        FakeConnectionStatus conn,
                        DebounceFlusher debounce) Build()
        {
            var ipc = new FakeIpcClient();
            var clock = new FakeClock();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Connected);
            var logger = new FakeDiagnosticsLogger();
            var lightList = new LightListState(ipc, logger);
            var stageCatalog = new StageCatalogState(ipc, logger);
            var volumeCache = new VolumeSchemaCache(ipc, logger);
            var debounce = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);
            var diag = new StageTabDiagnostics(logger);
            var vm = new StageLightingVolumeTabViewModel(
                ipc, ipc, conn, new FakePresetStorage(),
                lightList, stageCatalog, volumeCache, debounce, clock, diag, logger);
            return (vm, ipc, clock, conn, debounce);
        }

        [Test]
        public void StageCatalog_IsObservedByViewModel()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();

            ipc.Emit(StageLightingTopics.StageCatalog, new StageCatalogDto(new List<StageCatalogEntryDto>
            {
                new StageCatalogEntryDto("stages/a", "Stage A", null),
            }));

            Assert.That(vm.StageCatalog, Has.Count.EqualTo(1));
            Assert.That(vm.StageCatalog[0].DisplayName, Is.EqualTo("Stage A"));
        }

        [Test]
        public void StageSwitch_PublishesEvent_AndIsLoadedAcksClearSwitchingFlag()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();

            vm.SwitchStage("stages/a");

            // VM should have published the stage/command event.
            bool sentLoad = false;
            foreach (var s in ipc.Sent)
            {
                if (s.Topic == StageLightingTopics.StageCommand
                    && s.Kind == MessageKind.Event
                    && s.Payload is StageCommandDto sc && sc.Op == "load")
                {
                    sentLoad = true;
                    break;
                }
            }
            Assert.That(sentLoad, Is.True);
            Assert.That(vm.IsSwitchingStage, Is.True);

            ipc.Emit(StageLightingTopics.StageLoaded, new StageCurrentDto("stages/a"), MessageKind.Event);

            Assert.That(vm.IsSwitchingStage, Is.False);
            Assert.That(vm.StageCurrent.AddressableKey, Is.EqualTo("stages/a"));
        }

        [Test]
        public void LightAddRoundTrip_RegistersLightInList()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();

            vm.AddLight(new LightInitialDto(
                LightTypeDto.Directional, new Vector3Dto(0, 0, 0),
                new ColorDto(1, 1, 1, 1), 1f, 0f, 30f, "Sun"));

            // Simulate the main output side responding with a light/added + lights/list snapshot.
            ipc.Emit(StageLightingTopics.LightAdded,
                new LightAddedDto("light-1",
                    new LightInitialDto(LightTypeDto.Directional, new Vector3Dto(0, 0, 0),
                        new ColorDto(1, 1, 1, 1), 1f, 0f, 30f, "Sun")),
                MessageKind.Event);
            ipc.Emit(StageLightingTopics.LightsList, new LightListDto(new List<LightListItemDto>
            {
                new LightListItemDto("light-1", "Sun", LightTypeDto.Directional),
            }));

            Assert.That(vm.Lights, Has.Count.EqualTo(1));
            Assert.That(vm.Lights[0].LightId, Is.EqualTo("light-1"));
        }

        [Test]
        public void LightPropertyState_IsPublishedOnUpdate()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();
            ipc.Sent.Clear();

            vm.UpdateLightProperty("light-1", StageLightingTopics.PropertyIntensity, 2.5f);

            Assert.That(ipc.Sent[0].Topic, Is.EqualTo("light/light-1/intensity"));
            Assert.That(ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
            Assert.That(ipc.Sent[0].Payload, Is.EqualTo(2.5f));
        }

        [Test]
        public void LightRemove_PublishesRemoveEvent()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();
            ipc.Sent.Clear();

            vm.RemoveLight("light-1");

            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
            var dto = (LightCommandDto)ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("remove"));
            Assert.That(dto.LightId, Is.EqualTo("light-1"));
        }

        [Test]
        public void VolumeOverrideEnabled_PublishesPerTypeState()
        {
            var (vm, ipc, _, _, _) = Build();
            vm.OnActivated();
            ipc.Sent.Clear();

            vm.SetVolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom", true);

            Assert.That(ipc.Sent[0].Topic,
                Is.EqualTo("volume/override/UnityEngine.Rendering.Universal.Bloom/enabled"));
            Assert.That(ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
            Assert.That(ipc.Sent[0].Payload, Is.EqualTo(true));
        }

        [Test]
        public async Task VolumeSchema_FetchesViaRequestAsync_AndCaches()
        {
            var (vm, ipc, _, _, _) = Build();
            ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());
            vm.OnActivated();
            await Task.Delay(40);

            Assert.That(vm.VolumeSchemaIsLoaded, Is.True);
            Assert.That(ipc.Requests, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(ipc.Requests[0].Topic, Is.EqualTo(StageLightingTopics.VolumeOverrideSchema));
        }
    }
}
