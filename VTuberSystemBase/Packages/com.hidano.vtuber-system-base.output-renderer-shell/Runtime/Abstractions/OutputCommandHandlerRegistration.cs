#nullable enable
using System;
using System.Threading;

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// <c>IOutputCommandDispatcher.Register*Handler</c> が返却するハンドラ登録解除トークン（Req 3.3 / 4.5 / 4.6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各タブ spec はトークンを保持し、タブ非アクティブ化時に <see cref="Dispose"/> でハンドラを解除する。
    /// 多重 Dispose は安全（2 回目以降は no-op）。
    /// </para>
    /// <para>
    /// 本トークン自体はディスパッチャ内部の状態を直接保持せず、登録時に渡された解除コールバックを 1 度だけ呼び出す
    /// 単純な仲介役。具体的な解除処理は <c>HandlerRegistry</c> 側で実装される（Task 4.1）。
    /// </para>
    /// </remarks>
    public sealed class OutputCommandHandlerRegistration : IDisposable
    {
        private Action? _onDispose;

        /// <summary>登録解除コールバックを伴うトークンを生成する。</summary>
        /// <param name="onDispose">Dispose 時に 1 度だけ呼び出されるコールバック。<c>null</c> 不可。</param>
        /// <exception cref="ArgumentNullException"><paramref name="onDispose"/> が <c>null</c> の場合。</exception>
        public OutputCommandHandlerRegistration(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        /// <summary>既に Dispose 済みの場合 <c>true</c>。</summary>
        public bool IsDisposed => Volatile.Read(ref _onDispose) == null;

        /// <summary>登録を解除する。多重呼び出しは安全（2 回目以降は no-op）。</summary>
        public void Dispose()
        {
            var callback = Interlocked.Exchange(ref _onDispose, null);
            callback?.Invoke();
        }
    }
}
