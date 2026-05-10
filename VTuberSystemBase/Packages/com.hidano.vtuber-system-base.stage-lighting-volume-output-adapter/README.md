# Stage / Lighting / Volume Output Adapter

`stage-lighting-volume-tab` の IPC コントラクトをメイン出力側に橋渡しする UPM パッケージ。`output-renderer-shell` の `IOutputCommandDispatcher` / `IOutputSceneRoots` 経由で Stage 切替（Addressables）/ Light 動的追加 / URP VolumeProfile Override 反映 / プレビューカメラ + RenderTexture 提供を担当する。

## IL2CPP / link.xml

URP の `VolumeParameter<T>` 派生型をリフレクションで読み書きするため、本パッケージは `link.xml` を同梱して `Unity.RenderPipelines.Core.Runtime` / `Unity.RenderPipelines.Universal.Runtime` の strip を抑止する。

利用者プロジェクトが独自 `VolumeComponent` を追加する場合は、当該プロジェクトの `Assets/link.xml` に対応する `<assembly fullname="..." preserve="all" />` を追加する必要がある。

利用者プロジェクト用 `link.xml` の例（自前 VolumeComponent を含むアセンブリ名が `MyGame.PostFx` の場合）:

```xml
<linker>
  <assembly fullname="MyGame.PostFx" preserve="all" />
</linker>
```

### IL2CPP ビルド検証（手動）

1. `Build Settings > Player > IL2CPP` を選択し、`Bloom` / `Tonemapping` / `ColorAdjustments` を含む URP 設定でスタンドアロンビルドを行う。
2. ビルド成果物を起動し、メイン出力カメラに対して `volume/override/UnityEngine.Rendering.Universal.Bloom/intensity = 1.5f` を publish する。
3. ログに `[StageLightingVolumeOutputAdapter] VolumeParameterReflectionSetter.field_not_found` 等の警告が出ないこと、および Bloom が視覚的に効いていることを確認する。

## セットアップ概要

1. `OutputSceneBootstrapper` を持つシーンに本パッケージの `StageLightingVolumeOutputAdapterBootstrapper` MonoBehaviour を 1 つ `AddComponent` する。
2. Addressables Group に `stage` ラベル付きの Stage Prefab を登録する。
3. PlayMode 起動でアダプタが各 Handler を構築 / 登録し、`stage/*`, `light/*`, `volume/override/*`, `preview/*` トピックを処理する。
