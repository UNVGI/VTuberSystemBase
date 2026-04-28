#nullable enable
using System;
using System.Threading;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// Captures and queries the Unity main thread for dispatch verification. Tests typically capture
    /// the main thread ID once during <c>[SetUp]</c> on the test thread (which is the Unity main thread
    /// for EditMode tests) and then assert that callbacks land on the same thread.
    /// </summary>
    public static class MainThreadAffinity
    {
        private static int capturedThreadId;
        private static bool isCaptured;

        /// <summary>Records the current thread as the "main thread" for subsequent affinity checks.</summary>
        public static void Capture()
        {
            capturedThreadId = Thread.CurrentThread.ManagedThreadId;
            isCaptured = true;
        }

        /// <summary>Forgets the captured main-thread ID. Tests should call this from <c>[TearDown]</c>.</summary>
        public static void Reset()
        {
            capturedThreadId = 0;
            isCaptured = false;
        }

        /// <summary>The thread ID captured by <see cref="Capture"/>. Throws if no capture has been recorded.</summary>
        public static int CapturedThreadId
        {
            get
            {
                if (!isCaptured) throw new InvalidOperationException("MainThreadAffinity.Capture has not been called.");
                return capturedThreadId;
            }
        }

        /// <summary>Returns the current managed thread ID at the call site.</summary>
        public static int CurrentThreadId => Thread.CurrentThread.ManagedThreadId;

        /// <summary>True when the calling thread matches the most recently captured main thread.</summary>
        public static bool IsOnCapturedThread => isCaptured && Thread.CurrentThread.ManagedThreadId == capturedThreadId;

        /// <summary>Per-instance recorder; useful when several callbacks each need their own thread record.</summary>
        public sealed class Recorder
        {
            private int observedThreadId;
            private int callCount;
            public int ObservedThreadId => observedThreadId;
            public int CallCount => callCount;
            public bool WasInvoked => callCount > 0;

            public void Record()
            {
                observedThreadId = Thread.CurrentThread.ManagedThreadId;
                Interlocked.Increment(ref callCount);
            }

            public bool Matches(int expectedThreadId) => WasInvoked && observedThreadId == expectedThreadId;
        }
    }
}
