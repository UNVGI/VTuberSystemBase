#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Configuration;

namespace VTuberSystemBase.CoreIpc.Core.Lifecycle
{
    public static class RuntimeBootstrap
    {
        internal const string LogTag = "[CoreIpc.RuntimeBootstrap]";

        private static readonly object s_sync = new();
        private static bool s_quitSubscribed;
        private static bool s_isBootstrapped;
        private static Task? s_lastInitializationTask;

        public static bool IsBootstrapped
        {
            get { lock (s_sync) return s_isBootstrapped; }
        }

        public static Task? LastInitializationTask
        {
            get { lock (s_sync) return s_lastInitializationTask; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            try
            {
                Bootstrap(
                    optionsLoader: CoreIpcConfigLoader.Load,
                    runtimeFactory: () => new CoreIpcRuntimeHost(),
                    quitHandlerAttacher: AttachApplicationQuittingOnce,
                    initFailureLogger: ex => Debug.LogError(
                        $"{LogTag} CoreIpcRuntime initialization failed: {ex}"),
                    initSuccessLogger: () => Debug.Log(
                        $"{LogTag} CoreIpcRuntime initialization completed."));
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} bootstrap threw before initialization could start: {ex}");
            }
        }

        public static (ICoreIpcRuntime Runtime, Task InitializationTask) Bootstrap(
            Func<CoreIpcOptions> optionsLoader,
            Func<ICoreIpcRuntime> runtimeFactory,
            Action<ICoreIpcRuntime>? quitHandlerAttacher = null,
            Action<Exception>? initFailureLogger = null,
            Action? initSuccessLogger = null)
        {
            if (optionsLoader is null) throw new ArgumentNullException(nameof(optionsLoader));
            if (runtimeFactory is null) throw new ArgumentNullException(nameof(runtimeFactory));

            var options = optionsLoader();
            if (options is null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} options loader returned null; cannot start CoreIpcRuntime.");
            }

            var runtime = runtimeFactory();
            if (runtime is null)
            {
                throw new InvalidOperationException(
                    $"{LogTag} runtime factory returned null; cannot start CoreIpcRuntime.");
            }

            quitHandlerAttacher?.Invoke(runtime);

            Task initTask;
            try
            {
                initTask = runtime.InitializeAsync(options);
            }
            catch (Exception ex)
            {
                initFailureLogger?.Invoke(ex);
                throw;
            }

            _ = initTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        initFailureLogger?.Invoke(t.Exception?.GetBaseException() ?? t.Exception!);
                    }
                    else if (t.IsCompletedSuccessfully)
                    {
                        initSuccessLogger?.Invoke();
                    }
                },
                TaskScheduler.Default);

            lock (s_sync)
            {
                s_isBootstrapped = true;
                s_lastInitializationTask = initTask;
            }

            return (runtime, initTask);
        }

        public static void ResetForTesting()
        {
            lock (s_sync)
            {
                s_isBootstrapped = false;
                s_lastInitializationTask = null;
            }
        }

        private static void AttachApplicationQuittingOnce(ICoreIpcRuntime runtime)
        {
            lock (s_sync)
            {
                if (s_quitSubscribed) return;
                s_quitSubscribed = true;
            }
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            try
            {
                CoreIpcRuntime.Current?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"{LogTag} dispose during Application.quitting threw: {ex.Message}");
            }
        }
    }
}
