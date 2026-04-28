#nullable enable
using System;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Diagnostics
{
    /// <summary>
    /// <see cref="IOutputDiagnostics"/> の実装。<see cref="OutputSceneInitPhase"/> の単調遷移
    /// （+ <see cref="OutputSceneInitPhase.Failed"/> への脱出）を強制し、ディスプレイ割当・直近エラー・登録ハンドラ数
    /// の参照を提供する（Req 2.4a / 9.8）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 書き込み API は本 spec 内コンポーネントが利用する想定で <c>public</c> にしているが、
    /// 単調遷移ロジックを内蔵することで誤った書き込みを Fail-Fast で拒否する。
    /// 書き込みと読み取りはすべて <c>lock</c> で保護されるため、任意スレッドから安全に呼び出せる。
    /// </para>
    /// <para>
    /// <see cref="RegisteredHandlerCount"/> はディスパッチャの内部状態を反映するため、
    /// <see cref="AttachHandlerCountProvider"/> でカウント取得関数を注入する設計。
    /// 未注入時は 0 を返す。<see cref="Reset"/> でプロバイダ参照を解除し、PlayMode 反復時の
    /// 古いディスパッチャ参照のリークを防ぐ。
    /// </para>
    /// </remarks>
    public sealed class OutputDiagnostics : IOutputDiagnostics
    {
        private readonly object _gate = new();
        private OutputSceneInitPhase _currentPhase = OutputSceneInitPhase.Uninitialized;
        private DisplayAssignmentInfo _currentDisplayAssignment;
        private string? _lastErrorMessage;
        private long _lastErrorAtUnixMs;
        private Func<int>? _handlerCountProvider;

        /// <inheritdoc />
        public OutputSceneInitPhase CurrentPhase
        {
            get { lock (_gate) { return _currentPhase; } }
        }

        /// <inheritdoc />
        public DisplayAssignmentInfo CurrentDisplayAssignment
        {
            get { lock (_gate) { return _currentDisplayAssignment; } }
        }

        /// <inheritdoc />
        public int RegisteredHandlerCount
        {
            get
            {
                var provider = _handlerCountProvider;
                return provider?.Invoke() ?? 0;
            }
        }

        /// <inheritdoc />
        public string? LastErrorMessage
        {
            get { lock (_gate) { return _lastErrorMessage; } }
        }

        /// <inheritdoc />
        public long LastErrorAtUnixMs
        {
            get { lock (_gate) { return _lastErrorAtUnixMs; } }
        }

        /// <summary>
        /// 初期化フェーズを <paramref name="newPhase"/> へ進める。
        /// 単調遷移（より小さい序数への戻り）は <see cref="InvalidOperationException"/> で拒否する。
        /// <see cref="OutputSceneInitPhase.Failed"/> への遷移は任意フェーズから許容される。
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// 逆方向遷移（<paramref name="newPhase"/> の序数が <see cref="CurrentPhase"/> より小さい）が試行された場合。
        /// <see cref="Reset"/> を経由した <see cref="OutputSceneInitPhase.Uninitialized"/> 復帰のみが例外。
        /// </exception>
        public void AdvancePhase(OutputSceneInitPhase newPhase)
        {
            lock (_gate)
            {
                if (newPhase == OutputSceneInitPhase.Failed)
                {
                    _currentPhase = OutputSceneInitPhase.Failed;
                    return;
                }

                if (_currentPhase == OutputSceneInitPhase.Failed)
                {
                    throw new InvalidOperationException(
                        $"Cannot transition from Failed to {newPhase}. Call Reset() before re-initializing.");
                }

                if ((int)newPhase < (int)_currentPhase)
                {
                    throw new InvalidOperationException(
                        $"Reverse phase transition rejected: {_currentPhase} -> {newPhase}.");
                }

                _currentPhase = newPhase;
            }
        }

        /// <summary>
        /// ディスプレイ割当情報を更新する。<see cref="IDisplayRoutingService"/> 実装が
        /// <c>Activate</c> 完了時に呼び出す（Req 2.4a）。
        /// </summary>
        public void SetDisplayAssignment(DisplayAssignmentInfo info)
        {
            lock (_gate)
            {
                _currentDisplayAssignment = info;
            }
        }

        /// <summary>
        /// エラー情報を記録し、フェーズを <see cref="OutputSceneInitPhase.Failed"/> に遷移させる。
        /// <see cref="LastErrorMessage"/> と <see cref="LastErrorAtUnixMs"/> が同時に更新される。
        /// </summary>
        /// <param name="errorMessage">エラーメッセージ。null は <c>string.Empty</c> として記録される。</param>
        /// <param name="unixMs">エラー発生時刻（Unix エポックミリ秒）。</param>
        public void RecordError(string errorMessage, long unixMs)
        {
            lock (_gate)
            {
                _lastErrorMessage = errorMessage ?? string.Empty;
                _lastErrorAtUnixMs = unixMs;
                _currentPhase = OutputSceneInitPhase.Failed;
            }
        }

        /// <summary>
        /// 登録ハンドラ数の取得関数を注入する。<see cref="OutputSceneBootstrapper"/> が起動時に
        /// <c>() =&gt; dispatcher.RegisteredHandlerCount</c> を渡す。
        /// </summary>
        /// <param name="provider">取得関数。null を渡すとプロバイダ未設定状態に戻る。</param>
        public void AttachHandlerCountProvider(Func<int>? provider)
        {
            _handlerCountProvider = provider;
        }

        /// <summary>
        /// 全状態を初期値（<see cref="OutputSceneInitPhase.Uninitialized"/>、空の割当・エラー・プロバイダ）に戻す。
        /// PlayMode 反復時のクリーンアップ・OnDestroy で呼び出される（Req 6.3 / 6.4）。
        /// </summary>
        public void Reset()
        {
            lock (_gate)
            {
                _currentPhase = OutputSceneInitPhase.Uninitialized;
                _currentDisplayAssignment = default;
                _lastErrorMessage = null;
                _lastErrorAtUnixMs = 0;
                _handlerCountProvider = null;
            }
        }
    }
}
