#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// <see cref="OutputSceneBootstrapper"/> が <see cref="IDisplayRoutingService"/> を解決する際に
    /// 選択する実装の種別（Wave 3e）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定は <see cref="BuiltIn"/>（後方互換）。<see cref="RuntimeDisplaySelector"/> を選択すると
    /// <c>com.hidano.runtime-display-selector</c> パッケージの <c>RuntimeDisplaySelector.Current</c>
    /// Facade 経由で物理ディスプレイ振り分け / Spout センダー登録 / JSON 永続化が利用可能になる。
    /// </para>
    /// <para>
    /// テスト時に <c>OverrideServices</c> で <see cref="IDisplayRoutingService"/> 実装を直接注入した場合、
    /// 本 enum の値は無視される（Composition Root で注入が優先される）。
    /// </para>
    /// </remarks>
    public enum DisplayRoutingProvider
    {
        /// <summary>
        /// <c>BuiltInDisplayRoutingService</c>（<c>UnityEngine.Display.displays[n].Activate()</c> ベースの暫定実装）を使用する。
        /// 既定値であり、Wave 3a〜3d の後方互換を保つ。
        /// </summary>
        BuiltIn = 0,

        /// <summary>
        /// <c>RuntimeDisplaySelectorRoutingService</c>（<c>com.hidano.runtime-display-selector</c> Facade 経由）を使用する。
        /// 物理ディスプレイ経路に加えて Klak Spout センダーで OBS への直送を選択可能（Wave 3e）。
        /// </summary>
        RuntimeDisplaySelector = 1,
    }
}
