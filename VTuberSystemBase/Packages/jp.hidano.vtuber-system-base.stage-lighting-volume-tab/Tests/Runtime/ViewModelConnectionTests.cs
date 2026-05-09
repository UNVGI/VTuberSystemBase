#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks IPC disconnection / reconnection failsafes (Task 5.8, Requirements 9.4,
    /// 9.5, 9.8, 9.9, 9.10).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelConnectionTests
    {
        [Test]
        public void Commands_WhileDisconnected_AreSuppressed_AndWarn()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: false);
            ctx.vm.OnActivated();
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;
            ctx.ipc.Sent.Clear();

            ctx.vm.SwitchStage("stages/a");

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnIpcDisconnected));
            Assert.That(ctx.ipc.Sent, Is.Empty);
        }

        [Test]
        public void IsConnected_FollowsConnectionStatus()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: false);
            ctx.vm.OnActivated();
            Assert.That(ctx.vm.IsConnected, Is.False);

            ctx.conn.SetStatus(ConnectionStatusCode.Connected);

            Assert.That(ctx.vm.IsConnected, Is.True);
        }

        [Test]
        public void Send_WithBackingFailure_LogsAndWarns_WithoutCrashing()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;
            ctx.ipc.ForceFail = true;
            ctx.ipc.FailWith = new SendError(SendErrorCode.PayloadTooLarge);

            ctx.vm.SetVolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom", true);

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnSendFailed));
        }
    }
}
