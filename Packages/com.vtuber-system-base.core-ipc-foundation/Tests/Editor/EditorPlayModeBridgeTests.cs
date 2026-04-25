#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Lifecycle;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class EditorPlayModeBridgeTests
    {
        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
        }

        [Test]
        public void HandlePlayModeStateChange_NullRuntimeAccessor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: null!,
                    playerLoopUninstall: () => { }));
        }

        [Test]
        public void HandlePlayModeStateChange_NullUninstallAction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => null,
                    playerLoopUninstall: null!));
        }

        [Test]
        public void ExitingPlayMode_DisposesCurrentRuntime_AndCallsPlayerLoopUninstall()
        {
            var runtime = new RecordingRuntime();
            int uninstallCalls = 0;

            EditorPlayModeBridge.HandlePlayModeStateChange(
                PlayModeStateChange.ExitingPlayMode,
                currentRuntimeAccessor: () => runtime,
                playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls));

            Assert.AreEqual(1, runtime.DisposeCount,
                "ExitingPlayMode must dispose the current runtime exactly once.");
            Assert.AreEqual(1, uninstallCalls,
                "ExitingPlayMode must call PlayerLoopInstaller.Uninstall exactly once.");
        }

        [Test]
        public void ExitingPlayMode_WithNullCurrentRuntime_StillCallsPlayerLoopUninstall()
        {
            int uninstallCalls = 0;

            EditorPlayModeBridge.HandlePlayModeStateChange(
                PlayModeStateChange.ExitingPlayMode,
                currentRuntimeAccessor: () => null,
                playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls));

            Assert.AreEqual(1, uninstallCalls,
                "Even when no runtime is current, PlayerLoop uninstall must still run for symmetry.");
        }

        [Test]
        public void NonExitingPlayMode_DoesNotDisposeOrUninstall()
        {
            var states = new[]
            {
                PlayModeStateChange.EnteredEditMode,
                PlayModeStateChange.ExitingEditMode,
                PlayModeStateChange.EnteredPlayMode,
            };

            foreach (var state in states)
            {
                var runtime = new RecordingRuntime();
                int uninstallCalls = 0;

                EditorPlayModeBridge.HandlePlayModeStateChange(
                    state,
                    currentRuntimeAccessor: () => runtime,
                    playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls));

                Assert.AreEqual(0, runtime.DisposeCount,
                    $"Runtime must not be disposed for {state}.");
                Assert.AreEqual(0, uninstallCalls,
                    $"PlayerLoop uninstall must not be invoked for {state}.");
            }
        }

        [Test]
        public void ExitingPlayMode_RuntimeDisposeThrows_StillCallsUninstall_AndLogsWarning()
        {
            var runtime = new ThrowingRuntime();
            int uninstallCalls = 0;
            var warnings = new List<string>();

            Assert.DoesNotThrow(() =>
                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => runtime,
                    playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls),
                    logWarning: warnings.Add));

            Assert.AreEqual(1, uninstallCalls,
                "PlayerLoop uninstall must run even when runtime dispose threw.");
            Assert.IsTrue(warnings.Count >= 1,
                "A warning must be logged when the runtime dispose threw.");
            StringAssert.Contains("EditorPlayModeBridge", warnings[0]);
        }

        [Test]
        public void ExitingPlayMode_PlayerLoopUninstallThrows_LogsWarning_AndDoesNotPropagate()
        {
            var runtime = new RecordingRuntime();
            var warnings = new List<string>();

            Assert.DoesNotThrow(() =>
                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => runtime,
                    playerLoopUninstall: () => throw new InvalidOperationException("boom"),
                    logWarning: warnings.Add));

            Assert.AreEqual(1, runtime.DisposeCount,
                "Runtime must be disposed before uninstall is attempted.");
            Assert.IsTrue(warnings.Count >= 1,
                "A warning must be logged when the uninstall threw.");
        }

        [Test]
        public void ExitingPlayMode_AfterTwoSequentialCalls_DisposesTwice_AndUninstallsTwice()
        {
            var runtime = new RecordingRuntime();
            int uninstallCalls = 0;

            EditorPlayModeBridge.HandlePlayModeStateChange(
                PlayModeStateChange.ExitingPlayMode,
                currentRuntimeAccessor: () => runtime,
                playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls));

            EditorPlayModeBridge.HandlePlayModeStateChange(
                PlayModeStateChange.ExitingPlayMode,
                currentRuntimeAccessor: () => runtime,
                playerLoopUninstall: () => Interlocked.Increment(ref uninstallCalls));

            Assert.AreEqual(2, runtime.DisposeCount);
            Assert.AreEqual(2, uninstallCalls);
        }

        [Test]
        public void IsSubscribed_AfterStaticConstructor_IsTrue()
        {
            // [InitializeOnLoad] runs the static constructor when the editor domain loads.
            // Touching any static member ensures the type is initialized.
            EditorPlayModeBridge.EnsureSubscribed();
            Assert.IsTrue(EditorPlayModeBridge.IsSubscribed,
                "EditorPlayModeBridge must subscribe to playModeStateChanged on domain load.");
        }

        [Test]
        public void EnsureSubscribed_IsIdempotent()
        {
            EditorPlayModeBridge.EnsureSubscribed();
            EditorPlayModeBridge.EnsureSubscribed();
            EditorPlayModeBridge.EnsureSubscribed();

            Assert.IsTrue(EditorPlayModeBridge.IsSubscribed,
                "Multiple EnsureSubscribed calls must not break the subscription state.");
        }

        // ---------- Helpers ----------

        private sealed class RecordingRuntime : ICoreIpcRuntime
        {
            public int DisposeCount { get; private set; }

            public RuntimeState State => RuntimeState.NotInitialized;
            public ICoreIpcBus Bus =>
                throw new NotSupportedException("RecordingRuntime has no bus.");
            public CoreIpcOptions Options { get; } = new();

            public Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Dispose() => DisposeCount++;
        }

        private sealed class ThrowingRuntime : ICoreIpcRuntime
        {
            public RuntimeState State => RuntimeState.NotInitialized;
            public ICoreIpcBus Bus =>
                throw new NotSupportedException("ThrowingRuntime has no bus.");
            public CoreIpcOptions Options { get; } = new();

            public Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Dispose() => throw new InvalidOperationException("dispose-failure-for-tests");
        }
    }
}
