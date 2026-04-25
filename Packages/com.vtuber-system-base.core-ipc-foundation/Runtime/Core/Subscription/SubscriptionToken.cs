#nullable enable
using System;
using System.Threading;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Subscription
{
    public sealed class SubscriptionToken : ISubscriptionToken
    {
        private Action? _onDispose;
        private int _disposed;

        internal SubscriptionToken(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            var callback = _onDispose;
            _onDispose = null;
            callback?.Invoke();
        }
    }
}
