#nullable enable
using System;
using System.Threading;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime
{
    /// <summary>
    /// Asserts that the calling thread is the Unity main thread (Requirement
    /// 10.1〜10.5). Initialised by the bootstrapper at <c>Awake</c> time.
    /// </summary>
    public static class MainThreadGuard
    {
        private static int _mainThreadId;
        private static bool _initialized;

        public static bool IsInitialized => _initialized;
        public static int MainThreadId => _mainThreadId;

        /// <summary>Records the current thread as the Unity main thread.</summary>
        public static void Initialize()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _initialized = true;
        }

        /// <summary>Throws when called off the recorded main thread.</summary>
        public static void AssertMainThread()
        {
            if (!_initialized) return; // No-op until the bootstrapper initialises.
            var current = Thread.CurrentThread.ManagedThreadId;
            if (current != _mainThreadId)
            {
                throw new InvalidOperationException(
                    $"MainThreadGuard violation: expected thread #{_mainThreadId}, got #{current}.");
            }
        }

        public static void Reset()
        {
            _mainThreadId = 0;
            _initialized = false;
        }
    }
}
