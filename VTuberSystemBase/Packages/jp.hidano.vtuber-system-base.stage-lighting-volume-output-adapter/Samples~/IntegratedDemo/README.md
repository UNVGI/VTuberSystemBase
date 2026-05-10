# Stage / Lighting / Volume Output Adapter — Integrated Demo

最小構成での動作確認手順。手動検証 (Requirement 10.6) を兼ねる。

## 前提

- Unity 6.3 (6000.3+) プロジェクトで URP 17.x が有効化されていること。
- 以下 UPM パッケージがプロジェクトに追加されていること（manifest.json または Packages/ embedded）：
  - `com.hidano.vtuber-system-base.core-ipc-foundation`
  - `com.hidano.vtuber-system-base.output-renderer-shell`
  - `jp.hidano.vtuber-system-base.stage-lighting-volume-tab`
  - `jp.hidano.vtuber-system-base.stage-lighting-volume-output-adapter` (本パッケージ)
  - `com.hidano.scene-view-style-camera-controller`
  - `com.unity.addressables`

## シーン構築

1. 新規シーンに `OutputSceneBootstrapper` MonoBehaviour を持つ GameObject を配置（必須）。
2. 同シーンに `StageLightingVolumeOutputAdapterBootstrapper` MonoBehaviour を `AddComponent` する。
3. 利用者プロジェクトで `ICoreIpcBusProvider` を実装した MonoBehaviour（例: `MyIpcBusProvider : MonoBehaviour, ICoreIpcBusProvider { public ICoreIpcBus Bus => ...; }`）を同シーンに配置する。
4. Addressables Group に `stage` ラベル付きの Stage Prefab を 1 つ以上登録する（`SampleAddressablesGroup.md` 参照）。

## 動作確認

- PlayMode 開始でメイン出力に `StageRoot` / `LightsRoot` / `CamerasRoot` / `VolumesRoot` が生成され、`StagePreviewHostLocator.Current` が non-null になる。
- `volume/override/UnityEngine.Rendering.Universal.Bloom/enabled` を `true` で publish するとメイン出力カメラに Bloom が反映される。
- `light/command` event を `{op:"add", initial:{...}}` で publish するとシーンに新しい Light が生成され、`light/added` / `lights/list` が UI 側に届く。
- `preview/command` event を `{op:"set-enabled", enabled:false}` で publish すると PreviewCamera が描画停止する。

## トラブルシューティング

- 起動時警告 `dependencies_missing` → `OutputSceneBootstrapper` が同シーンに無いか、`Dispatcher` / `Roots` がまだ初期化されていない。`AdapterStartupRegistration.WaitForOutputSceneAndStart` を Coroutine から呼ぶ。
- 起動時警告 `ipc_bus_missing` → `ICoreIpcBusProvider` を実装する MonoBehaviour が見つかっていない。`Bus` プロパティが non-null を返すか確認。
- IL2CPP ビルド後に Volume Override が反映されない → 利用者プロジェクト独自の `VolumeComponent` がある場合、対応する `link.xml` をプロジェクトの `Assets/link.xml` に追加する。
