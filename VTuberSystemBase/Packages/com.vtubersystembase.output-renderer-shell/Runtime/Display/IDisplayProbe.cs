#nullable enable
using UnityEngine;

namespace VTuberSystemBase.OutputRendererShell.Display
{
    /// <summary>
    /// <see cref="UnityEngine.Display"/> 静的 API への薄いラッパ。物理ディスプレイ依存テストの差し替え点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BuiltInDisplayRoutingService"/> は本インタフェース経由で <c>Display.displays</c> を参照する。
    /// PlayMode テストで「Display N が存在しない」状況を再現するためのスタブ実装を差し替えられる。
    /// </para>
    /// <para>
    /// 実装側で <see cref="Display.displays"/>[index].<see cref="Display.Activate()"/> 相当の副作用を起こす。
    /// テスト用スタブでは副作用を記録するだけでよい。
    /// </para>
    /// </remarks>
    public interface IDisplayProbe
    {
        /// <summary>現在の物理ディスプレイ数（<c>Display.displays.Length</c> 相当）。</summary>
        int DisplayCount { get; }

        /// <summary>Editor PlayMode 上で動作している場合 <c>true</c>（<c>Application.isEditor</c> 相当）。</summary>
        bool IsEditor { get; }

        /// <summary>
        /// <c>Display.displays[index].Activate()</c> 相当の副作用を起こす。
        /// <paramref name="index"/> が範囲外の場合は何もしない（呼び出し元で範囲判定済み）。
        /// </summary>
        void ActivateDisplay(int index);

        /// <summary>
        /// Standalone 環境のみ <c>Screen.fullScreenMode</c> を設定する。Editor では no-op。
        /// </summary>
        void SetFullScreenMode(FullScreenMode mode);
    }
}
