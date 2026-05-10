#nullable enable
using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Runtime
{
    [TestFixture]
    public sealed class MainThreadGuardTests
    {
        [TearDown]
        public void TearDown() => MainThreadGuard.Reset();

        [UnityTest]
        public IEnumerator InitializeAndAssert_OnMainThread_DoesNotThrow()
        {
            yield return null;
            MainThreadGuard.Initialize();
            Assert.That(MainThreadGuard.IsInitialized, Is.True);
            Assert.DoesNotThrow(() => MainThreadGuard.AssertMainThread());
        }

        [UnityTest]
        public IEnumerator AssertOnWorkerThread_Throws()
        {
            yield return null;
            MainThreadGuard.Initialize();
            Exception? captured = null;
            var thread = new Thread(() =>
            {
                try { MainThreadGuard.AssertMainThread(); }
                catch (Exception ex) { captured = ex; }
            });
            thread.Start();
            thread.Join();
            Assert.That(captured, Is.InstanceOf<InvalidOperationException>());
        }
    }
}
