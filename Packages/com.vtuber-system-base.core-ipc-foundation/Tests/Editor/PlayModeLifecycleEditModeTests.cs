#nullable enable
using NUnit.Framework;
using UnityEditor;
using VTuberSystemBase.CoreIpc.Core;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class PlayModeLifecycleEditModeTests
    {
        [SetUp]
        public void SetUp()
        {
            // Each EditMode test starts from a clean singleton state so we can
            // observe the absence of an auto-started runtime independently of
            // any prior test that may have registered one.
            CoreIpcRuntime.ResetForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
        }

        [Test]
        public void EditMode_DoesNotAutoStartCoreIpcRuntime()
        {
            Assert.IsFalse(EditorApplication.isPlaying,
                "Sanity check: this EditMode test must run while the editor is NOT in Play Mode.");

            Assert.IsNull(CoreIpcRuntime.Current,
                "RuntimeInitializeOnLoadMethod(BeforeSceneLoad) must not fire in Edit Mode "
                + "(Req 4.8); CoreIpcRuntime.Current must remain null. Observed an active "
                + "runtime in state "
                + (CoreIpcRuntime.Current?.State.ToString() ?? "<null>") + ".");
        }
    }
}
