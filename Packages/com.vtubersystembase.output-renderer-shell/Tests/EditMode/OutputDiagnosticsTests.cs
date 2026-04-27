#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;
using VTuberSystemBase.OutputRendererShell.Dispatch;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests
{
    /// <summary>
    /// Task 5.1: <see cref="OutputDiagnostics"/> の (a) 単調遷移成功, (b) 逆方向遷移拒否,
    /// (c) Failed 記録時の LastError 更新, (d) RegisteredHandlerCount のディスパッチャ反映 を検証する
    /// （Req 2.4a / 9.8）。
    /// </summary>
    [TestFixture]
    public class OutputDiagnosticsTests
    {
        [Test]
        [Description("既定状態は Uninitialized / 既定 DisplayAssignmentInfo / 0 件 / null エラー")]
        public void Defaults_AreUninitializedAndEmpty()
        {
            var sut = new OutputDiagnostics();
            Assert.AreEqual(OutputSceneInitPhase.Uninitialized, sut.CurrentPhase);
            Assert.AreEqual(default(DisplayAssignmentInfo), sut.CurrentDisplayAssignment);
            Assert.AreEqual(0, sut.RegisteredHandlerCount);
            Assert.IsNull(sut.LastErrorMessage);
            Assert.AreEqual(0L, sut.LastErrorAtUnixMs);
        }

        [Test]
        [Description("(a) Uninitialized → RootsCreated → ... → Complete の単調遷移を許容")]
        public void AdvancePhase_MonotonicForwardSequence_Succeeds()
        {
            var sut = new OutputDiagnostics();
            var sequence = new[]
            {
                OutputSceneInitPhase.RootsCreated,
                OutputSceneInitPhase.CameraReady,
                OutputSceneInitPhase.LightReady,
                OutputSceneInitPhase.VolumeReady,
                OutputSceneInitPhase.IpcServerReady,
                OutputSceneInitPhase.DispatcherReady,
                OutputSceneInitPhase.DisplayRouted,
                OutputSceneInitPhase.Complete,
            };
            foreach (var phase in sequence)
            {
                sut.AdvancePhase(phase);
                Assert.AreEqual(phase, sut.CurrentPhase);
            }
        }

        [Test]
        [Description("(b) より小さい序数のフェーズへの遷移は InvalidOperationException で拒否")]
        public void AdvancePhase_ReverseTransition_Throws()
        {
            var sut = new OutputDiagnostics();
            sut.AdvancePhase(OutputSceneInitPhase.CameraReady);

            Assert.Throws<InvalidOperationException>(() =>
                sut.AdvancePhase(OutputSceneInitPhase.RootsCreated));
            Assert.AreEqual(OutputSceneInitPhase.CameraReady, sut.CurrentPhase,
                "拒否時に CurrentPhase は変化しないこと");
        }

        [Test]
        [Description("同一フェーズへの再代入は許容（境界値）")]
        public void AdvancePhase_SamePhaseTransition_IsAllowed()
        {
            var sut = new OutputDiagnostics();
            sut.AdvancePhase(OutputSceneInitPhase.CameraReady);
            Assert.DoesNotThrow(() => sut.AdvancePhase(OutputSceneInitPhase.CameraReady));
        }

        [Test]
        [Description("Failed への遷移は任意フェーズから許容される")]
        public void AdvancePhase_AnyToFailed_IsAllowed()
        {
            var sut = new OutputDiagnostics();
            sut.AdvancePhase(OutputSceneInitPhase.LightReady);
            sut.AdvancePhase(OutputSceneInitPhase.Failed);
            Assert.AreEqual(OutputSceneInitPhase.Failed, sut.CurrentPhase);
        }

        [Test]
        [Description("Failed 状態からの正常フェーズ遷移は Reset() なしには拒否される")]
        public void AdvancePhase_FromFailedWithoutReset_Throws()
        {
            var sut = new OutputDiagnostics();
            sut.AdvancePhase(OutputSceneInitPhase.Failed);
            Assert.Throws<InvalidOperationException>(() =>
                sut.AdvancePhase(OutputSceneInitPhase.Complete));
        }

        [Test]
        [Description("(c) RecordError で LastErrorMessage / LastErrorAtUnixMs が更新され、Failed に遷移すること")]
        public void RecordError_UpdatesLastErrorAndTransitionsToFailed()
        {
            var sut = new OutputDiagnostics();
            sut.AdvancePhase(OutputSceneInitPhase.CameraReady);

            sut.RecordError("camera factory failed", 1_700_000_000_000L);

            Assert.AreEqual(OutputSceneInitPhase.Failed, sut.CurrentPhase);
            Assert.AreEqual("camera factory failed", sut.LastErrorMessage);
            Assert.AreEqual(1_700_000_000_000L, sut.LastErrorAtUnixMs);
        }

        [Test]
        [Description("RecordError は null メッセージを string.Empty として記録する")]
        public void RecordError_NullMessage_RecordsAsEmpty()
        {
            var sut = new OutputDiagnostics();
            sut.RecordError(null!, 100L);
            Assert.AreEqual(string.Empty, sut.LastErrorMessage);
            Assert.AreEqual(100L, sut.LastErrorAtUnixMs);
        }

        [Test]
        [Description("(d) AttachHandlerCountProvider 経由でディスパッチャの登録数が反映されること")]
        public void AttachHandlerCountProvider_ReflectsDispatcherRegistrationCount()
        {
            var sut = new OutputDiagnostics();
            using var dispatcher = new OutputCommandDispatcher(new OutputShellLogger(LogLevel.Verbose));

            sut.AttachHandlerCountProvider(() => dispatcher.RegisteredHandlerCount);
            Assert.AreEqual(0, sut.RegisteredHandlerCount);

            using var stateToken = dispatcher.RegisterStateHandler<int>("topic.s", _ => { });
            Assert.AreEqual(1, sut.RegisteredHandlerCount);

            using var eventToken = dispatcher.RegisterEventHandler<int>("topic.e", _ => { });
            Assert.AreEqual(2, sut.RegisteredHandlerCount);

            stateToken.Dispose();
            Assert.AreEqual(1, sut.RegisteredHandlerCount, "登録解除も反映されること");
        }

        [Test]
        [Description("AttachHandlerCountProvider に null を渡すと未注入状態（0）に戻ること")]
        public void AttachHandlerCountProvider_NullDetachesProvider()
        {
            var sut = new OutputDiagnostics();
            sut.AttachHandlerCountProvider(() => 5);
            Assert.AreEqual(5, sut.RegisteredHandlerCount);

            sut.AttachHandlerCountProvider(null);
            Assert.AreEqual(0, sut.RegisteredHandlerCount);
        }

        [Test]
        [Description("SetDisplayAssignment で割当情報が反映されること")]
        public void SetDisplayAssignment_UpdatesCurrentDisplayAssignment()
        {
            var sut = new OutputDiagnostics();
            var info = new DisplayAssignmentInfo
            {
                RequestedDisplayIndex = 1,
                EffectiveDisplayIndex = 0,
                IsFallbackActive = true,
                IsEditorLimitedMode = false,
                DiagnosticMessage = "fallback to display 0",
            };
            sut.SetDisplayAssignment(info);

            Assert.AreEqual(info, sut.CurrentDisplayAssignment);
        }

        [Test]
        [Description("Reset で全状態が初期値へ戻り、再度フェーズ遷移を開始できること（PlayMode 反復対応）")]
        public void Reset_ReturnsToInitialState_AndAllowsReuse()
        {
            var sut = new OutputDiagnostics();
            sut.AttachHandlerCountProvider(() => 3);
            sut.SetDisplayAssignment(new DisplayAssignmentInfo { RequestedDisplayIndex = 1 });
            sut.RecordError("err", 42L);

            sut.Reset();

            Assert.AreEqual(OutputSceneInitPhase.Uninitialized, sut.CurrentPhase);
            Assert.AreEqual(default(DisplayAssignmentInfo), sut.CurrentDisplayAssignment);
            Assert.IsNull(sut.LastErrorMessage);
            Assert.AreEqual(0L, sut.LastErrorAtUnixMs);
            Assert.AreEqual(0, sut.RegisteredHandlerCount);

            // Reset 後は再度フェーズ遷移を進められること
            Assert.DoesNotThrow(() => sut.AdvancePhase(OutputSceneInitPhase.RootsCreated));
            Assert.AreEqual(OutputSceneInitPhase.RootsCreated, sut.CurrentPhase);
        }
    }
}
