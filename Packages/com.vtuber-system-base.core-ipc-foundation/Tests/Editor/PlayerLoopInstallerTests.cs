#nullable enable
using System;
using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class PlayerLoopInstallerTests
    {
        private PlayerLoopSystem _originalLoop;

        [SetUp]
        public void SetUp()
        {
            _originalLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
            PlayerLoop.SetPlayerLoop(_originalLoop);
        }

        [Test]
        public void Install_NullAction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PlayerLoopInstaller.Install(null!));
        }

        [Test]
        public void Install_AddsIpcDispatchStepUnderPreUpdate()
        {
            Assert.IsFalse(PlayerLoopInstaller.IsInstalled);

            PlayerLoopInstaller.Install(() => { });

            Assert.IsTrue(PlayerLoopInstaller.IsInstalled);
            Assert.IsTrue(LoopContainsIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void Uninstall_RemovesIpcDispatchStep()
        {
            PlayerLoopInstaller.Install(() => { });
            Assert.IsTrue(PlayerLoopInstaller.IsInstalled);

            PlayerLoopInstaller.Uninstall();

            Assert.IsFalse(PlayerLoopInstaller.IsInstalled);
            Assert.IsFalse(LoopContainsIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void Uninstall_WhenNotInstalled_IsNoOp()
        {
            Assert.IsFalse(PlayerLoopInstaller.IsInstalled);

            Assert.DoesNotThrow(() => PlayerLoopInstaller.Uninstall());
            Assert.IsFalse(PlayerLoopInstaller.IsInstalled);
            Assert.IsFalse(LoopContainsIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void Install_Twice_LogsWarningAndReplaces()
        {
            int warningCount = 0;
            string? capturedMessage = null;

            int firstAction = 0;
            int secondAction = 0;

            PlayerLoopInstaller.Install(
                () => firstAction++,
                logWarning: _ => warningCount++);
            PlayerLoopInstaller.Install(
                () => secondAction++,
                logWarning: msg =>
                {
                    warningCount++;
                    capturedMessage = msg;
                });

            Assert.AreEqual(1, warningCount, "Second install while installed must emit a single warning.");
            Assert.IsNotNull(capturedMessage);
            StringAssert.Contains("already installed", capturedMessage!);

            // Only one IpcDispatchStep should remain after replacement.
            Assert.AreEqual(1, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));

            // The most recent flushAction should be the one invoked.
            InvokeIpcDispatchStepDelegates(PlayerLoop.GetCurrentPlayerLoop());
            Assert.AreEqual(0, firstAction, "Replaced flush action must not be invoked.");
            Assert.AreEqual(1, secondAction, "Newly installed flush action must be invoked.");
        }

        [Test]
        public void RepeatedInstallUninstall_RemainsSymmetric()
        {
            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(PlayerLoopInstaller.IsInstalled, $"Iter {i}: must start uninstalled");

                PlayerLoopInstaller.Install(() => { });
                Assert.IsTrue(PlayerLoopInstaller.IsInstalled, $"Iter {i}: installed");
                Assert.AreEqual(1, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()), $"Iter {i}: exactly one step");

                PlayerLoopInstaller.Uninstall();
                Assert.IsFalse(PlayerLoopInstaller.IsInstalled, $"Iter {i}: uninstalled");
                Assert.AreEqual(0, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()), $"Iter {i}: no step left");
            }
        }

        [Test]
        public void Install_InvokingUpdateDelegate_CallsFlushAction()
        {
            int invocations = 0;

            PlayerLoopInstaller.Install(() => invocations++);

            InvokeIpcDispatchStepDelegates(PlayerLoop.GetCurrentPlayerLoop());
            InvokeIpcDispatchStepDelegates(PlayerLoop.GetCurrentPlayerLoop());

            Assert.AreEqual(2, invocations);
        }

        [Test]
        public void Uninstall_AfterInstall_ResetsIsInstalledFlag()
        {
            PlayerLoopInstaller.Install(() => { });
            PlayerLoopInstaller.Uninstall();
            PlayerLoopInstaller.Uninstall();

            Assert.IsFalse(PlayerLoopInstaller.IsInstalled);
        }

        [Test]
        public void Install_PreservesExistingPreUpdateChildren()
        {
            var original = PlayerLoop.GetCurrentPlayerLoop();
            int originalChildCount = CountPreUpdateChildren(original);

            PlayerLoopInstaller.Install(() => { });

            int newChildCount = CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop());
            Assert.AreEqual(originalChildCount + 1, newChildCount);
        }

        [Test]
        public void Uninstall_RestoresOriginalChildCount()
        {
            int originalChildCount = CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop());

            PlayerLoopInstaller.Install(() => { });
            PlayerLoopInstaller.Uninstall();

            int restoredChildCount = CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop());
            Assert.AreEqual(originalChildCount, restoredChildCount);
        }

        private static bool LoopContainsIpcDispatchStepUnderPreUpdate(PlayerLoopSystem loop)
        {
            return CountIpcDispatchStepUnderPreUpdate(loop) > 0;
        }

        private static int CountIpcDispatchStepUnderPreUpdate(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return 0;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                var children = loop.subSystemList[i].subSystemList;
                if (children is null) return 0;
                int count = 0;
                for (int j = 0; j < children.Length; j++)
                {
                    if (children[j].type == typeof(IpcDispatchStep)) count++;
                }
                return count;
            }
            return 0;
        }

        private static int CountPreUpdateChildren(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return 0;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                return loop.subSystemList[i].subSystemList?.Length ?? 0;
            }
            return 0;
        }

        private static void InvokeIpcDispatchStepDelegates(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                var children = loop.subSystemList[i].subSystemList;
                if (children is null) return;
                for (int j = 0; j < children.Length; j++)
                {
                    if (children[j].type == typeof(IpcDispatchStep))
                    {
                        children[j].updateDelegate?.Invoke();
                    }
                }
                return;
            }
        }
    }
}
