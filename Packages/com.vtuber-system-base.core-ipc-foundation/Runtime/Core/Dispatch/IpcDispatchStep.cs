#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Core.Dispatch
{
    public sealed class IpcDispatchStep
    {
        private readonly MainThreadDispatchQueue _queue;
        private readonly Action<string, Exception>? _logError;

        public IpcDispatchStep(MainThreadDispatchQueue queue, Action<string, Exception>? logError = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logError = logError;
        }

        public bool IsInstalled => PlayerLoopInstaller.IsInstalled;

        public void Tick()
        {
            try
            {
                _queue.Flush();
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    "IpcDispatchStep.Flush threw an unexpected exception: " +
                    ex.GetType().Name + ": " + ex.Message,
                    ex);
            }
        }

        public void Install(Action<string>? logWarning = null)
        {
            PlayerLoopInstaller.Install(Tick, logWarning);
        }

        public void Uninstall()
        {
            PlayerLoopInstaller.Uninstall();
        }
    }
}
