#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class RuntimeAssemblySmokeTests
    {
        [Test]
        public void AbstractionsAssembly_IsReferencedFromRuntimeTests()
        {
            Assert.IsNotNull(typeof(ICoreIpcBus).Assembly);
            Assert.AreEqual("VTuberSystemBase.CoreIpc.Abstractions", typeof(ICoreIpcBus).Namespace);
        }

        [UnityTest]
        public IEnumerator PlayModeRunner_ExecutesSingleFrameTest()
        {
            yield return null;
            Assert.Pass();
        }
    }
}
