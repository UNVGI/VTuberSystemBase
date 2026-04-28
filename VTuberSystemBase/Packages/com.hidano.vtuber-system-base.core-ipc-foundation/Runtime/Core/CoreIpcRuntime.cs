#nullable enable
using System;
using System.Threading;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core
{
    public static class CoreIpcRuntime
    {
        private static readonly object s_sync = new();
        private static ICoreIpcRuntime? s_current;

        public static ICoreIpcRuntime? Current
        {
            get
            {
                lock (s_sync) return s_current;
            }
        }

        public static void OverrideForTesting(ICoreIpcRuntime runtime)
        {
            if (runtime is null) throw new ArgumentNullException(nameof(runtime));
            lock (s_sync) s_current = runtime;
        }

        public static void ResetForTesting()
        {
            lock (s_sync) s_current = null;
        }

        internal static void SetCurrent(ICoreIpcRuntime runtime)
        {
            if (runtime is null) throw new ArgumentNullException(nameof(runtime));
            lock (s_sync) s_current = runtime;
        }

        internal static void ClearCurrent(ICoreIpcRuntime runtime)
        {
            lock (s_sync)
            {
                if (ReferenceEquals(s_current, runtime)) s_current = null;
            }
        }
    }
}
