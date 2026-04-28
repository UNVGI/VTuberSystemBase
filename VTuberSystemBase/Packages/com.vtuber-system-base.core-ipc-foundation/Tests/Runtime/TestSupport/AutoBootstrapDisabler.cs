#nullable enable
using UnityEngine;
using VTuberSystemBase.CoreIpc.Core.Lifecycle;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    /// <summary>
    /// Disables <see cref="RuntimeBootstrap.OnBeforeSceneLoad"/>'s auto-bootstrap so the
    /// Unity Test Runner never starts a CoreIpcRuntime that races with test-owned
    /// loopback hosts. The hook fires at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>,
    /// which Unity invokes strictly before <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>,
    /// so the production hook short-circuits via the disabled flag without ever calling
    /// <see cref="RuntimeBootstrap.Bootstrap"/>. This file lives in a test-only assembly
    /// (<c>defineConstraints: UNITY_INCLUDE_TESTS</c>) and is therefore never compiled
    /// into shipped builds.
    /// </summary>
    internal static class AutoBootstrapDisabler
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DisableAutoBootstrapDuringTests()
        {
            RuntimeBootstrap.DisableAutoBootstrap();
        }
    }
}
