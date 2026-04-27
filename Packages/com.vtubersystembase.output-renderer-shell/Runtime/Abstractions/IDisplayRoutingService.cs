#nullable enable
using System;
using UnityEngine;

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力カメラを物理ディスプレイへ割り当てるサービスの抽象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 暫定実装は本 spec 内 <c>BuiltInDisplayRoutingService</c>（task 3.2）が <c>Display.displays[n].Activate()</c>
    /// で提供する。将来的には spec #7 RuntimeDisplaySelectorIntegration がモニタ EDID 等を考慮した
    /// 差し替え実装を提供する想定で、本インタフェースが差し替え接合点となる（Req 2.5 / 2.6 / 8.5）。
    /// </para>
    /// <para>
    /// 契約：<see cref="Activate(Camera, DisplayRoutingConfig)"/> は
    /// 引数 <see cref="Camera.targetDisplay"/> を実装側で設定し、結果を
    /// <see cref="DisplayAssignmentInfo"/> として返す。<see cref="GetAssignment"/> は
    /// 直近の <see cref="Activate(Camera, DisplayRoutingConfig)"/> 結果を等価に返す。
    /// </para>
    /// <para>
    /// 本インタフェースの実装は <see cref="IDisposable"/> を実装し、保持しているリソース
    /// （Editor 警告ログのスロットリング状態等）を解放可能とする（Req 8.5）。
    /// </para>
    /// </remarks>
    public interface IDisplayRoutingService : IDisposable
    {
        /// <summary>
        /// <paramref name="camera"/> を <paramref name="config"/> に従って物理ディスプレイへ割り当てる。
        /// </summary>
        /// <param name="camera">割り当て対象のメイン出力カメラ。null 不可。</param>
        /// <param name="config">割り当て構成（既定値は Display 2 / FullScreenWindow）。</param>
        /// <returns>適用された割り当て結果（フォールバック発生有無を含む）。</returns>
        DisplayAssignmentInfo Activate(Camera camera, DisplayRoutingConfig config);

        /// <summary>
        /// 直近の <see cref="Activate(Camera, DisplayRoutingConfig)"/> 結果を返す。未呼び出しの場合は <c>default</c>。
        /// </summary>
        DisplayAssignmentInfo GetAssignment();

        /// <summary>
        /// 要求ディスプレイ不在による Display 0 フォールバックが発生中の場合 <c>true</c>（Req 2.4 / 2.4a）。
        /// 未呼び出し時は <c>false</c>。
        /// </summary>
        bool IsFallbackActive { get; }
    }
}
