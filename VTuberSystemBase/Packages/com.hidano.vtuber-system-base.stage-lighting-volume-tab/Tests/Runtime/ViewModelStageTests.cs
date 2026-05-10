#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks Stage command behaviour (Task 5.2, Requirements 3.4, 3.5, 3.7, 3.8, 3.11, 9.2).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelStageTests
    {
        [Test]
        public void SwitchStage_PublishesLoadEvent_AndSetsIsSwitchingTrue()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            ctx.vm.SwitchStage("stages/concert");

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ctx.ipc.Sent[0].Topic, Is.EqualTo(StageLightingTopics.StageCommand));
            var payload = (StageCommandDto)ctx.ipc.Sent[0].Payload!;
            Assert.That(payload.Op, Is.EqualTo("load"));
            Assert.That(payload.AddressableKey, Is.EqualTo("stages/concert"));
            Assert.That(ctx.vm.IsSwitchingStage, Is.True);
        }

        [Test]
        public void SwitchStage_WhileAlreadySwitching_RaisesWarning_AndDoesNotResend()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.SwitchStage("stages/a");
            ctx.ipc.Sent.Clear();

            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            ctx.vm.SwitchStage("stages/b");

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnStageInProgress));
            Assert.That(ctx.ipc.Sent, Is.Empty);
        }

        [Test]
        public void StageLoaded_ClearsIsSwitching()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.SwitchStage("stages/a");

            ctx.ipc.Emit(StageLightingTopics.StageLoaded, new StageCurrentDto("stages/a"), MessageKind.Event);

            Assert.That(ctx.vm.IsSwitchingStage, Is.False);
            Assert.That(ctx.vm.StageCurrent.AddressableKey, Is.EqualTo("stages/a"));
        }

        [Test]
        public void StageLoadFailed_RaisesWarning_AndClearsIsSwitching()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.SwitchStage("stages/a");
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            ctx.ipc.Emit(StageLightingTopics.StageLoadFailed,
                new StageLoadFailedDto("stages/a", "load_failed", "boom"),
                MessageKind.Event);

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnStageLoadFailed));
            Assert.That(ctx.vm.IsSwitchingStage, Is.False);
        }

        [Test]
        public void UnloadStage_PublishesUnloadEvent()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            ctx.vm.UnloadStage();

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            var payload = (StageCommandDto)ctx.ipc.Sent[0].Payload!;
            Assert.That(payload.Op, Is.EqualTo("unload"));
            Assert.That(payload.AddressableKey, Is.Null);
        }
    }
}
