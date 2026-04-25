#if UNITY_EDITOR
#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Core.Lifecycle
{
    [InitializeOnLoad]
    public static class EditorPlayModeBridge
    {
        internal const string LogTag = "[CoreIpc.EditorPlayModeBridge]";

        private static readonly object s_sync = new();
        private static bool s_subscribed;

        static EditorPlayModeBridge()
        {
            EnsureSubscribed();
        }

        public static bool IsSubscribed
        {
            get { lock (s_sync) return s_subscribed; }
        }

        public static void EnsureSubscribed()
        {
            lock (s_sync)
            {
                if (s_subscribed) return;
                EditorApplication.playModeStateChanged += OnPlayModeStateChangedDefault;
                s_subscribed = true;
            }
        }

        public static void ResetForTesting()
        {
            lock (s_sync)
            {
                if (!s_subscribed) return;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChangedDefault;
                s_subscribed = false;
            }
        }

        public static void HandlePlayModeStateChange(
            PlayModeStateChange change,
            Func<ICoreIpcRuntime?> currentRuntimeAccessor,
            Action playerLoopUninstall,
            Action<string>? logWarning = null)
        {
            if (currentRuntimeAccessor is null) throw new ArgumentNullException(nameof(currentRuntimeAccessor));
            if (playerLoopUninstall is null) throw new ArgumentNullException(nameof(playerLoopUninstall));

            if (change != PlayModeStateChange.ExitingPlayMode) return;

            var runtime = currentRuntimeAccessor();
            if (runtime is not null)
            {
                try
                {
                    runtime.Dispose();
                }
                catch (Exception ex)
                {
                    var msg = $"{LogTag} runtime dispose during ExitingPlayMode threw: {ex.Message}";
                    if (logWarning is not null) logWarning(msg);
                    else Debug.LogWarning(msg);
                }
            }

            try
            {
                playerLoopUninstall();
            }
            catch (Exception ex)
            {
                var msg = $"{LogTag} PlayerLoop uninstall during ExitingPlayMode threw: {ex.Message}";
                if (logWarning is not null) logWarning(msg);
                else Debug.LogWarning(msg);
            }
        }

        private static void OnPlayModeStateChangedDefault(PlayModeStateChange change)
        {
            HandlePlayModeStateChange(
                change,
                static () => CoreIpcRuntime.Current,
                static () => PlayerLoopInstaller.Uninstall());
        }
    }
}
#endif
