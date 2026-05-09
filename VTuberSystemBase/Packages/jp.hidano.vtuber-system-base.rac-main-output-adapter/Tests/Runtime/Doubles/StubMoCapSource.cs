using System;
using RealtimeAvatarController.Core;
using UniRx;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// テスト用 <see cref="IMoCapSource"/>。<c>Initialize</c> でフラグを立てるのみで MotionStream は何も流さない。
    /// </summary>
    public sealed class StubMoCapSource : IMoCapSource
    {
        private readonly Subject<MotionFrame> _motionStream = new();
        private bool _initialized;
        private bool _disposed;

        /// <summary>初期化済みか。</summary>
        public bool IsInitialized => _initialized;

        /// <inheritdoc/>
        public string SourceType => "Stub";

        /// <inheritdoc/>
        public IObservable<MotionFrame> MotionStream => _motionStream;

        /// <inheritdoc/>
        public void Initialize(MoCapSourceConfigBase config)
        {
            _initialized = true;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _motionStream.OnCompleted();
            _motionStream.Dispose();
        }
    }
}
