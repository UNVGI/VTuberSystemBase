#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シーンのルート GameObject 命名規約定数（Req 1.1 / 1.7）。
    /// </summary>
    /// <remarks>
    /// 後続 spec（#4〜#6）は <c>IOutputSceneRoots</c> API もしくは本クラスの定数文字列のいずれを用いてもルート参照を解決可能。
    /// 命名はハードコードされ、本 spec 配下のすべての生成・テスト・サンプルで一貫する。
    /// </remarks>
    public static class OutputSceneRootNames
    {
        /// <summary>ステージアセット（Prefab）配置用ルート GameObject 名。</summary>
        public const string Stage = "StageRoot";

        /// <summary>キャラクター（アバター）配置用ルート GameObject 名。</summary>
        public const string Characters = "CharactersRoot";

        /// <summary>Light 配置用ルート GameObject 名（デフォルト Directional Light を配下に含む）。</summary>
        public const string Lights = "LightsRoot";

        /// <summary>Camera 配置用ルート GameObject 名（デフォルトカメラを配下に含む）。</summary>
        public const string Cameras = "CamerasRoot";

        /// <summary>Global Volume 配置用ルート GameObject 名（空の Global Volume を配下に含む）。</summary>
        public const string Volumes = "VolumeRoot";
    }
}
