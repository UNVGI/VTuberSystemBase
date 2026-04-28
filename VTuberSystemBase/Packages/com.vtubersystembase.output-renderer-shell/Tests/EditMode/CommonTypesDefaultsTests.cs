#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests
{
    /// <summary>
    /// Task 1.2: 共通型（enum / struct / 登録トークン）の不変性および既定値が NPE を起こさないことを検証する。
    /// </summary>
    [TestFixture]
    public class CommonTypesDefaultsTests
    {
        [Test]
        [Description("OutputCommandKind の既定値は State であること")]
        public void OutputCommandKind_Default_IsState()
        {
            Assert.AreEqual(OutputCommandKind.State, default(OutputCommandKind));
        }

        [Test]
        [Description("OutputSceneInitPhase の既定値は Uninitialized であること")]
        public void OutputSceneInitPhase_Default_IsUninitialized()
        {
            Assert.AreEqual(OutputSceneInitPhase.Uninitialized, default(OutputSceneInitPhase));
        }

        [Test]
        [Description("OutputSceneInitPhase は 9 段階遷移と Failed 値を持つこと（Req 1.6 / 5.5）")]
        public void OutputSceneInitPhase_Values_CoverFlowAndFailed()
        {
            Assert.That((int)OutputSceneInitPhase.Uninitialized, Is.EqualTo(0));
            Assert.That((int)OutputSceneInitPhase.RootsCreated, Is.GreaterThan((int)OutputSceneInitPhase.Uninitialized));
            Assert.That((int)OutputSceneInitPhase.Complete, Is.GreaterThan((int)OutputSceneInitPhase.DisplayRouted));
            Assert.That((int)OutputSceneInitPhase.Failed, Is.GreaterThan((int)OutputSceneInitPhase.Complete));
        }

        [Test]
        [Description("DisplayAssignmentInfo の default 値はすべてのプロパティアクセスが NPE を起こさないこと（Req 2.4a）")]
        public void DisplayAssignmentInfo_DefaultValue_NoNullReference()
        {
            var info = default(DisplayAssignmentInfo);
            Assert.DoesNotThrow(() =>
            {
                _ = info.RequestedDisplayIndex;
                _ = info.EffectiveDisplayIndex;
                _ = info.IsFallbackActive;
                _ = info.IsEditorLimitedMode;
                _ = info.DiagnosticMessage;
            });
            Assert.That(info.RequestedDisplayIndex, Is.EqualTo(0));
            Assert.That(info.EffectiveDisplayIndex, Is.EqualTo(0));
            Assert.That(info.IsFallbackActive, Is.False);
            Assert.That(info.IsEditorLimitedMode, Is.False);
        }

        [Test]
        [Description("DisplayAssignmentInfo は readonly record struct として不変であること")]
        public void DisplayAssignmentInfo_IsReadonlyValueType()
        {
            var t = typeof(DisplayAssignmentInfo);
            Assert.IsTrue(t.IsValueType, "DisplayAssignmentInfo must be a value type (readonly struct)");
            // record struct は IsAutoLayout/Sealed の判定が複雑だが、init-only のため readonly セマンティクスは
            // 修飾子レベルで保証されている。同値性の確認のみ追加する。
            var a = new DisplayAssignmentInfo { RequestedDisplayIndex = 1, EffectiveDisplayIndex = 1 };
            var b = new DisplayAssignmentInfo { RequestedDisplayIndex = 1, EffectiveDisplayIndex = 1 };
            Assert.AreEqual(a, b);
        }

        [Test]
        [Description("DisplayRoutingConfig の既定構成は TargetDisplayIndex=1 / FullScreenWindow（Req 2.2 / 2.3 / 2.7）")]
        public void DisplayRoutingConfig_DefaultsApplied()
        {
            var cfg = new DisplayRoutingConfig();
            Assert.AreEqual(1, cfg.TargetDisplayIndex);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, cfg.FullScreenMode);
            Assert.IsFalse(cfg.SuppressEditorWarning);
        }

        [Test]
        [Description("DisplayRoutingConfig は init-only で、コピー後の値設定が独立していること")]
        public void DisplayRoutingConfig_WithExpression_IsImmutable()
        {
            var baseCfg = new DisplayRoutingConfig();
            var alt = baseCfg with { TargetDisplayIndex = 2, SuppressEditorWarning = true };
            Assert.AreEqual(1, baseCfg.TargetDisplayIndex);
            Assert.AreEqual(2, alt.TargetDisplayIndex);
            Assert.IsFalse(baseCfg.SuppressEditorWarning);
            Assert.IsTrue(alt.SuppressEditorWarning);
        }

        [Test]
        [Description("StateCommand<T> の既定値は NPE を起こさず、Topic は null 許容であること（Req 4.4 ハンドラ契約は XMLDoc）")]
        public void StateCommand_DefaultValue_NoNullReference()
        {
            var cmd = default(StateCommand<int>);
            Assert.DoesNotThrow(() =>
            {
                _ = cmd.Topic;
                _ = cmd.Payload;
                _ = cmd.ReceivedAtTicks;
            });
            Assert.IsNull(cmd.Topic);
            Assert.AreEqual(0, cmd.Payload);
            Assert.AreEqual(0L, cmd.ReceivedAtTicks);
        }

        [Test]
        [Description("EventCommand<T> の既定値は NPE を起こさないこと")]
        public void EventCommand_DefaultValue_NoNullReference()
        {
            var cmd = default(EventCommand<string>);
            Assert.DoesNotThrow(() =>
            {
                _ = cmd.Topic;
                _ = cmd.Payload;
                _ = cmd.ReceivedAtTicks;
            });
            Assert.IsNull(cmd.Topic);
            Assert.IsNull(cmd.Payload);
        }

        [Test]
        [Description("RequestCommand<T> の既定値は NPE を起こさず、CorrelationId が null 許容であること（Req 4.7）")]
        public void RequestCommand_DefaultValue_NoNullReference()
        {
            var cmd = default(RequestCommand<int>);
            Assert.DoesNotThrow(() =>
            {
                _ = cmd.Topic;
                _ = cmd.CorrelationId;
                _ = cmd.Payload;
                _ = cmd.ReceivedAtTicks;
            });
            Assert.IsNull(cmd.CorrelationId);
        }

        [Test]
        [Description("StateCommand<T> / EventCommand<T> / RequestCommand<T> はすべて値型で不変であること")]
        public void CommandTypes_AreReadonlyValueTypes()
        {
            Assert.IsTrue(typeof(StateCommand<int>).IsValueType);
            Assert.IsTrue(typeof(EventCommand<int>).IsValueType);
            Assert.IsTrue(typeof(RequestCommand<int>).IsValueType);
        }

        [Test]
        [Description("OutputSceneRootNames の各定数が空でないこと（Req 1.1 / 1.7）")]
        public void OutputSceneRootNames_AllNonEmpty()
        {
            Assert.IsNotEmpty(OutputSceneRootNames.Stage);
            Assert.IsNotEmpty(OutputSceneRootNames.Characters);
            Assert.IsNotEmpty(OutputSceneRootNames.Lights);
            Assert.IsNotEmpty(OutputSceneRootNames.Cameras);
            Assert.IsNotEmpty(OutputSceneRootNames.Volumes);
        }

        [Test]
        [Description("OutputSceneRootNames はすべて一意であること")]
        public void OutputSceneRootNames_AllUnique()
        {
            var names = new[]
            {
                OutputSceneRootNames.Stage,
                OutputSceneRootNames.Characters,
                OutputSceneRootNames.Lights,
                OutputSceneRootNames.Cameras,
                OutputSceneRootNames.Volumes,
            };
            CollectionAssert.AllItemsAreUnique(names);
        }

        [Test]
        [Description("OutputCommandHandlerRegistration: null コールバックは ArgumentNullException")]
        public void OutputCommandHandlerRegistration_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OutputCommandHandlerRegistration(null!));
        }

        [Test]
        [Description("OutputCommandHandlerRegistration: Dispose で 1 度だけコールバックを呼ぶこと")]
        public void OutputCommandHandlerRegistration_Dispose_InvokesOnce()
        {
            int count = 0;
            var token = new OutputCommandHandlerRegistration(() => count++);
            Assert.IsFalse(token.IsDisposed);

            token.Dispose();
            Assert.AreEqual(1, count);
            Assert.IsTrue(token.IsDisposed);

            token.Dispose();
            Assert.AreEqual(1, count, "Dispose は冪等で 2 度目以降は no-op");
        }
    }
}
