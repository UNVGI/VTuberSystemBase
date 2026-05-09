# Implementation Plan

> 本タスクは design.md の「Components and Interfaces」「System Flows」「File Structure Plan」「Requirements Traceability」に基づく。依存方向は Contracts 参照 → Internal Helpers → Diagnostics → Domain Handlers → Preview MonoBehaviour → Composition Root を厳守する。TDD を基本とし、各 Handler / Setter 実装前にテストダブルと失敗テストを先行整備する。
>
> 各タスクの参照表記:
> - `_Requirements:` requirements.md の AC 番号
> - `_Boundary:` design.md の Boundary Commitments 上のオーナーコンポーネント
> - `_Depends:` 依存タスク番号

## 1. Foundation: パッケージ雛形と共通抽象の整備

- [x] 1.1 UPM パッケージ骨格と asmdef 境界の確立
  - UPM パッケージ `jp.hidano.vtuber-system-base.stage-lighting-volume-output-adapter` の `package.json` を作成し、`Unity 6.3` 最低バージョン、依存（`com.hidano.vtuber-system-base.core-ipc-foundation`, `com.hidano.vtuber-system-base.output-renderer-shell`, `jp.hidano.vtuber-system-base.stage-lighting-volume-tab`, `com.hidano.scene-view-style-camera-controller`, `com.unity.addressables`, `com.unity.render-pipelines.universal`）を宣言する。
  - `Runtime/`, `Tests/Editor/`, `Tests/PlayMode/` のフォルダ構造を File Structure Plan に従って作成する。
  - Runtime asmdef `VTuberSystemBase.StageLightingVolumeOutputAdapter.Runtime` を作成し、参照を `VTuberSystemBase.StageLightingVolumeTab.Contracts` / `VTuberSystemBase.OutputRendererShell.Runtime` / `VTuberSystemBase.CoreIpc.Abstractions` / `Unity.RenderPipelines.Universal.Runtime` / `Unity.RenderPipelines.Core.Runtime` / `Unity.Addressables` / `SceneViewStyleCameraController.Runtime` に限定する。`stage-lighting-volume-tab` の Runtime asmdef および `ui-toolkit-shell` への参照は禁止する。
  - Tests.Editor / Tests.PlayMode asmdef を Runtime とは独立に作成する。
  - `.meta` ファイルの GUID は `[guid]::NewGuid().ToString('N')` で都度ランダム生成（連続パターン禁止）。
  - 観測可能な完了条件: パッケージを Unity 6.3 リファレンスプロジェクトに配置するとコンパイルエラーなしでロードされ、禁止参照（UI 側 Runtime 等）を加えるとコンパイルエラーになることを確認できる。
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_
  - _Boundary: Package skeleton_

- [x] 1.2 link.xml と IL2CPP strip 抑止設定
  - パッケージルートに `link.xml` を配置し、`<assembly fullname="Unity.RenderPipelines.Core.Runtime" preserve="all" />` および `<assembly fullname="Unity.RenderPipelines.Universal.Runtime" preserve="all" />` を含めて URP の `VolumeComponent` / `VolumeParameter<T>` 派生型がリフレクションで参照されても strip されないようにする。
  - 利用者プロジェクト独自の `VolumeComponent` についてはユーザーが追加 link.xml で対応するよう README に明記する手順を入れる。
  - 観測可能な完了条件: IL2CPP スタンドアロンビルドで `Bloom` / `Tonemapping` / `ColorAdjustments` の各 `VolumeParameter<T>` フィールドにリフレクションで `value` 代入できることが手動検証できる。
  - _Requirements: 5.8, 10.8_
  - _Boundary: link.xml_
  - _Depends: 1.1_

- [x] 1.3 内部ヘルパ（DtoConverters / HandlerRegistrationToken）の実装
  - `DtoConverters` を実装し、`ColorDto ↔ UnityEngine.Color`、`Vector3Dto ↔ UnityEngine.Vector3`、`Vector3Dto → Quaternion (Euler)`、`Vector4Dto ↔ Vector4 / Vector2 / Vector3`、`LightTypeDto ↔ UnityEngine.LightType` の各変換ヘルパを提供する。
  - `HandlerRegistrationToken : IDisposable` を実装し、複数の `IDisposable` を子として保持して `Dispose` で逆順に Dispose する composite pattern を実装する。二重 Dispose は no-op。
  - 観測可能な完了条件: ラウンドトリップテスト（DTO → Unity 型 → DTO で値が一致）と composite Dispose のテストが緑になる。
  - _Requirements: 4.4, 4.5, 4.6, 2.3_
  - _Boundary: Internal/DtoConverters, Internal/HandlerRegistrationToken_
  - _Depends: 1.1_

- [x] 1.4 AdapterLogger と AdapterLoggerConfig の実装
  - `AdapterLogger` を実装し、`Verbose` / `Info` / `Warning` / `Error` の各メソッドが `[StageLightingVolumeOutputAdapter] {component}.{event}: {context}` 形式で `UnityEngine.Debug.Log/LogWarning/LogError` にのみ出力するようにする。
  - `AdapterLoggerConfig.MinLevel` で外部からログレベル切替を可能にする。既定は `Info`。
  - 構造化フィールド（topic, lightId, typeFullName, paramName, exception）を `string?` 引数で受け取り、null は出力しない。
  - 観測可能な完了条件: ログレベルを `Warning` に設定すると `Info` 出力が抑止される、`Error` 呼出で `LogError` が呼ばれることがテストで確認できる。
  - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - _Boundary: Diagnostics/AdapterLogger_
  - _Depends: 1.1_

- [x] 1.5 StageLightingVolumeOutputAdapterDiagnostics の実装
  - `StageLightingVolumeOutputAdapterDiagnostics` を実装し、`IsReady`, `RegisteredHandlerCount`, `CurrentStageAddressableKey`, `LightCount`, `VolumeOverrideTypeCount`, `PreviewHostReady`, `LastErrorMessage`, `LastErrorAtUnixMs` を読み取り専用プロパティで公開する（書込は internal set）。
  - `DiagnosticsSnapshot Capture()` でスナップショット record struct を返却する。
  - スレッドセーフ（プリミティブ型は volatile、参照型は lock）にする。
  - 観測可能な完了条件: 各プロパティ更新後の `Capture()` が同値を返すテストが緑になる。
  - _Requirements: 9.5, 9.6_
  - _Boundary: Diagnostics/StageLightingVolumeOutputAdapterDiagnostics_
  - _Depends: 1.1_

- [x] 1.6 テストダブル群の整備（TDD 基盤）
  - `FakeOutputCommandDispatcher` を実装：`RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` を記録、テストから `EmitState(topic, payload)` / `EmitEvent(topic, payload)` / `InvokeRequest(topic, request)` で受信を inject 可能、解除トークン Dispose 検証付き。`PublishedStates` / `PublishedEvents` リストで送信内容を観測可能（出力側 publish API を本 spec が呼ぶ場合の観測点）。
  - `FakeOutputSceneRoots : IOutputSceneRoots` を実装：`Stage`, `Lights`, `Cameras`, `Volumes` の各 Transform を内部 GameObject から作成、`GlobalVolumeProfile` は `ScriptableObject.CreateInstance<VolumeProfile>()` 生成、`DefaultCamera` は内部 Camera。`Dispose` で全 GameObject 破棄。
  - `FakeInstantiationProvider : IInstantiationProvider` を実装：`InstantiateAsync(addressableKey, parent)` の応答を辞書で設定可能（成功時は新 GameObject 返却、失敗時は `InstantiationResult.Success=false` + ErrorCode 設定）。`ReleaseInstance` 呼出を記録、`LoadResourceLocationsAsync(label)` 応答も差し替え可能。
  - 観測可能な完了条件: 各 Fake のセルフテストが通り、後続タスクから注入可能な形で Tests.Editor / Tests.PlayMode asmdef から参照できる。
  - _Requirements: 10.1, 10.3_
  - _Boundary: Tests/Editor/Fake*_
  - _Depends: 1.1_

## 2. Diagnostics & Error Reporting

- [x] 2.1 AdapterErrorReporter の実装
  - `AdapterErrorReporter` を実装し、`ReportLightError(string? lightId, string errorCode, string message)` で `LightErrorDto`（CorrelationId="" 既定）を構築して `light/error` event を publish、`ReportStageLoadFailed(string addressableKey, string errorCode, string message)` で `StageLoadFailedDto` を構築して `stage/load-failed` event を publish する。
  - `IOutputCommandDispatcher` 経由で publish できる場合は dispatcher、そうでない場合は `output-renderer-shell` 側の出力→UI 方向 publish API を利用する（実 API は実装時に確認、抽象を `IAdapterEventPublisher` として切り出す方針も取れる）。
  - publish と同時に `AdapterLogger.Error` で診断ログ出力、`StageLightingVolumeOutputAdapterDiagnostics.LastErrorMessage / LastErrorAtUnixMs` を更新する。
  - 観測可能な完了条件: `FakeOutputCommandDispatcher` 経由で `ReportLightError("abc", "internal_error", "msg")` を呼ぶと `light/error` topic に `LightErrorDto(LightId="abc", ErrorCode="internal_error", Message="msg")` が publish され、診断スナップショットが更新されることがテストで検証できる。
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7, 7.9_
  - _Boundary: Diagnostics/AdapterErrorReporter_
  - _Depends: 1.4, 1.5, 1.6_

## 3. Stage Domain

- [x] 3.1 IInstantiationProvider と AddressablesInstantiationProvider の実装
  - `IInstantiationProvider` interface を実装し、`InstantiateAsync(string addressableKey, Transform parent, CancellationToken ct)` / `ReleaseInstance(GameObject go)` / `LoadResourceLocationsAsync(string label, CancellationToken ct)` を定義する。
  - `AddressablesInstantiationProvider : IInstantiationProvider` を実装し、`Addressables.InstantiateAsync(addressableKey, parent, instantiateInWorldSpace: false)` を `Task<InstantiationResult>` でラップする。`AsyncOperationHandle<GameObject>.Completed` を購読し、`Status == Succeeded` なら `Success=true` + `Instance=handle.Result` を返し、`Status == Failed` なら `ErrorCode="load_failed"` または `"instantiate_failed"` + `ErrorMessage=handle.OperationException.Message` を返す。`InvalidKeyException` は `ErrorCode="not_found"`。
  - `ReleaseInstance` は `Addressables.ReleaseInstance(go)` を呼び、戻り値が false なら警告ログ。
  - `LoadResourceLocationsAsync(label)` は `Addressables.LoadResourceLocationsAsync(label, typeof(GameObject))` の結果を `IList<IResourceLocation>` として取得し、`PrimaryKey` の `IReadOnlyList<string>` に変換して返す。
  - 観測可能な完了条件: PlayMode で実 Addressables Group に登録した GameObject を `InstantiateAsync` → `ReleaseInstance` でラウンドトリップ、未登録キーで `Success=false, ErrorCode="not_found"` が返ることが確認できる。
  - _Requirements: 3.2, 3.3, 3.6, 3.7_
  - _Boundary: Stage/IInstantiationProvider, Stage/AddressablesInstantiationProvider_
  - _Depends: 1.6_

- [x] 3.2 StageCatalogBuilder の実装
  - `StageCatalogBuilder` を実装し、`BuildAsync(IInstantiationProvider provider)` で `provider.LoadResourceLocationsAsync(label: "stage")` を呼び、結果の各 `primaryKey` を `AddressableKey` とし、`DisplayName` は `primaryKey` をそのまま採用（実装フェーズで Addressables の `IResourceLocation.InternalId` 等を確認して改善余地あり）、`ThumbnailAddressableKey = $"{primaryKey}.thumbnail"` に設定して `StageCatalogEntryDto` 配列を構築、`StageCatalogDto(Items)` を返す。
  - 「stage」ラベル未登録時は空配列 + 警告ログ（`AdapterLogger.Warning("Stage label not found in Addressables")`）。
  - 例外時は `AdapterLogger.Error` + 空配列返却（描画継続）。
  - 観測可能な完了条件: `FakeInstantiationProvider` に複数 location を設定すると対応する `StageCatalogDto.Items.Count` が一致、空応答で空配列 + 警告ログが記録されることがテストで確認できる。
  - _Requirements: 3.1, 3.8_
  - _Boundary: Stage/StageCatalogBuilder_
  - _Depends: 3.1, 1.4_

- [x] 3.3 ActiveStageState の実装
  - `ActiveStageState` クラスを実装し、`CurrentStage : GameObject?` / `CurrentAddressableKey : string?` / `IsLoading : bool` を private set + public get で公開する。
  - `SetLoading(bool)`, `SetActive(GameObject stage, string key)`, `Clear()` を提供。
  - 観測可能な完了条件: 各遷移後のプロパティ値がテストで検証できる。
  - _Requirements: 3.2, 3.3_
  - _Boundary: Stage/ActiveStageState_
  - _Depends: 1.1_

- [x] 3.4 StageHandler の実装
  - `StageHandler` を実装し、コンストラクタで `IOutputCommandDispatcher`, `IOutputSceneRoots`, `IInstantiationProvider`, `StageCatalogBuilder`, `AdapterErrorReporter`, `AdapterLogger`, `StageLightingVolumeOutputAdapterDiagnostics` を受け取る。
  - `Start()` で以下を順次実行：
    1. `RegisterStateHandler<StageCommandDto>(StageLightingTopics.StageActive, ...)` 登録（Requirement 2.2, 3.10）
    2. `RegisterEventHandler<StageCommandDto>(StageLightingTopics.StageCommand, ...)` 登録
    3. `StageCatalogBuilder.BuildAsync` を fire-and-forget 起動 → 完了後に `PublishState(stage/catalog, dto)`
  - `HandleStageActive` / `HandleStageCommand` で payload `StageCommandDto.Op` に応じて分岐：
    - `"load"`：`ActiveStageState.SetLoading(true)` → `provider.InstantiateAsync(addressableKey, roots.Stage, ct)` → 完了確認 → 旧ステージ存在時に `provider.ReleaseInstance(oldGo)` → `ActiveStageState.SetActive(newGo, key)` → `PublishState(stage/current, new StageCurrentDto(key))` → `PublishEvent(stage/loaded, new StageCurrentDto(key))` → `Diagnostics.CurrentStageAddressableKey` 更新
    - `"unload"`：旧ステージ存在時 `provider.ReleaseInstance(oldGo)` → `ActiveStageState.Clear()` → `PublishState(stage/current, new StageCurrentDto(null))`
  - 失敗時は `AdapterErrorReporter.ReportStageLoadFailed(addressableKey, errorCode, message)` を呼び、`ActiveStageState` は変更しない（旧ステージ保持、Requirement 3.4）。
  - 全ハンドラを `try/catch` で囲み、例外時は `AdapterLogger.Error` + 描画継続（Requirement 3.9, 7.1）。
  - `Dispose()` で登録解除トークン全 Dispose、現 Stage GameObject の `ReleaseInstance` を呼ぶ（Requirement 3.7）。
  - 観測可能な完了条件: `FakeOutputCommandDispatcher` 経由の lazy swap シナリオテストで「load 成功 → 旧 ReleaseInstance → state/event publish 順序」、「load 失敗 → 旧保持 → load-failed publish」、「unload → 即時 Release → null state publish」の各シナリオが緑になる。
  - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9, 3.10, 7.1, 7.2_
  - _Boundary: Stage/StageHandler_
  - _Depends: 2.1, 3.1, 3.2, 3.3_

## 4. Light Domain

- [x] 4.1 LightTypeMapper の実装
  - `LightTypeMapper` を実装し、`ToUnity(LightTypeDto)` / `ToDto(UnityEngine.LightType)` の双方向マッピング（Directional/Point/Spot/Area）を提供する。
  - 観測可能な完了条件: 4 種すべてでラウンドトリップが一致するテストが緑になる。
  - _Requirements: 4.6_
  - _Boundary: Lights/LightTypeMapper_
  - _Depends: 1.3_

- [x] 4.2 LightRegistry の実装
  - `LightRegistry` を実装し、`Dictionary<string, LightEntry>` を内部に持ち、`TryGet(lightId, out entry)` / `Add(lightId, entry)` / `Remove(lightId)` / `ToListDto()` / `AllLightIds` / `Clear()` を提供する。
  - `ToListDto()` は安定順序（lightId 採番順 = 追加順）で `LightListItemDto` 配列を返す。
  - `Remove` 時は `LightEntry.PropertyHandlers` の全 `IDisposable` を Dispose する責務は呼び出し元（`LightHandler`）に委ねる（Registry 自身は GameObject 破棄も行わない、純データ構造）。
  - 観測可能な完了条件: Add / TryGet / Remove / Clear / ToListDto の各シナリオが安定順序維持を含めて緑になる。
  - _Requirements: 4.1, 4.2, 4.10, 4.12_
  - _Boundary: Lights/LightRegistry_
  - _Depends: 1.1_

- [x] 4.3 LightPropertyApplier の実装
  - `LightPropertyApplier` を実装し、各プロパティ毎の Apply メソッドを提供する：
    - `ApplyIntensity(string lightId, StateCommand<float> cmd)` → `LightRegistry.TryGet` → `entry.Light.intensity = cmd.Payload`
    - `ApplyColor(string lightId, StateCommand<ColorDto> cmd)` → `entry.Light.color = DtoConverters.ToUnity(cmd.Payload)`
    - `ApplyRotation(string lightId, StateCommand<Vector3Dto> cmd)` → `entry.GameObject.transform.localRotation = DtoConverters.ToQuaternion(cmd.Payload)`
    - `ApplyType(string lightId, StateCommand<LightTypeDto> cmd)` → `entry.Light.type = LightTypeMapper.ToUnity(cmd.Payload)`
    - `ApplyRange(string lightId, StateCommand<float> cmd)` → `entry.Light.range = cmd.Payload`
    - `ApplySpotAngle(string lightId, StateCommand<float> cmd)` → `entry.Light.spotAngle = cmd.Payload`
    - `ApplyDisplayName(string lightId, StateCommand<string> cmd)` → `entry.DisplayName` 更新（GameObject 名は `Light_{lightId}` のまま、Requirement 4.9）
  - 各メソッドは未知 lightId は警告ログのみで破棄、例外は `AdapterLogger.Error` で記録して描画継続。
  - 観測可能な完了条件: 実 `Light` コンポーネント + `LightRegistry` 経由で各プロパティ反映ラウンドトリップが緑になる。未知 lightId で警告ログが記録されることが確認できる。
  - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10, 7.1_
  - _Boundary: Lights/LightPropertyApplier_
  - _Depends: 1.3, 1.4, 4.1, 4.2_

- [x] 4.4 LightHandler の実装
  - `LightHandler` を実装し、コンストラクタで `IOutputCommandDispatcher`, `IOutputSceneRoots`, `AdapterErrorReporter`, `AdapterLogger`, `StageLightingVolumeOutputAdapterDiagnostics` を受け取る。
  - `Start()` で以下を実行：
    1. `RegisterEventHandler<LightCommandDto>(StageLightingTopics.LightCommand, OnLightCommand)` 登録
    2. `PublishState(StageLightingTopics.LightsList, new LightListDto(Items: []))` 初期 publish（Requirement 4.13）
  - `OnLightCommand`：`payload.Op` で分岐：
    - `"add"`：lightId = `Guid.NewGuid().ToString("N")` → `new GameObject($"Light_{lightId}")` → `transform.SetParent(roots.Lights)` → `AddComponent<UnityEngine.Light>()` → Initial（Type, Rotation, Color, Intensity, Range, SpotAngle）反映 → `LightRegistry.Add(lightId, entry)` → 7 プロパティ用 `RegisterStateHandler` 動的登録（intensity/color/rotation/type/range/spotAngle/displayName） → 戻り値 `IDisposable[]` を `LightEntry.PropertyHandlers` に保持 → `PublishEvent(light/added, new LightAddedDto(lightId, initial))` → `PublishState(lights/list, registry.ToListDto())`
    - `"remove"`：`LightRegistry.TryGet(payload.LightId, out entry)` → `entry.PropertyHandlers` 全 Dispose → `Object.Destroy(entry.GameObject)` → `LightRegistry.Remove(lightId)` → `PublishState(lights/list, registry.ToListDto())`。lightId が registry に存在しない場合は `AdapterErrorReporter.ReportLightError(lightId, "not_found", message)` で `light/error` publish + `lights/list` 再 publish（Requirement 7.4）。
  - 全 `try/catch` で囲み、Add 失敗時は `ReportLightError(null, "internal_error", message)`（Requirement 4.11, 7.3）。
  - `Dispose()` で全 `LightEntry` の handlers Dispose + GameObject Destroy + Registry Clear。Light 一覧 publish 用の最初の登録解除トークンも Dispose（Requirement 4.12）。
  - `Diagnostics.LightCount` を Add/Remove 毎に更新。
  - 観測可能な完了条件: `FakeOutputCommandDispatcher` 経由で add → lightId 採番（GUID 32 桁 hex）→ `light/added` publish → `lights/list` publish → 各プロパティハンドラ登録 → remove → handlers Dispose → GameObject 破棄 → `lights/list` 再 publish の順序が緑になる。
  - _Requirements: 4.1, 4.2, 4.11, 4.12, 4.13, 7.1, 7.3, 7.4_
  - _Boundary: Lights/LightHandler_
  - _Depends: 1.3, 1.6, 2.1, 4.2, 4.3_

## 5. Volume Domain

- [x] 5.1 VolumeOverrideRegistry の実装
  - `VolumeOverrideRegistry` を実装し、`Dictionary<string, Type> typeFullNameToType` と逆引き `Dictionary<Type, string> typeToFullName` を保持する。`Build(IReadOnlyList<Type> volumeComponentTypes)` で起動時に構築、`GetTypeByFullName(string typeFullName, out Type type)` / `Contains(typeFullName)` を提供する。
  - 観測可能な完了条件: 既知型での GetType 取得、未知型での miss が緑になる。
  - _Requirements: 5.2, 5.5_
  - _Boundary: Volume/VolumeOverrideRegistry_
  - _Depends: 1.1_

- [x] 5.2 VolumeParameterKindResolver の実装
  - `VolumeParameterKindResolver` を実装し、`Resolve(Type volumeParameterType) → ParamKind` で `BoolParameter → Bool`, `IntParameter / NoInterpIntParameter / ClampedIntParameter / MinIntParameter / MaxIntParameter → Int`, `FloatParameter / ClampedFloatParameter / NoInterpFloatParameter / MinFloatParameter / MaxFloatParameter → Float (or ClampedFloat for Clamped*)`, `ColorParameter → Color`, `Vector2Parameter → Vector2`, `Vector3Parameter → Vector3`, `Vector4Parameter → Vector4`, `VolumeParameter<TEnum> where TEnum : Enum → Enum`, それ以外 → `Unknown`。
  - リフレクションで `Type.IsSubclassOf(typeof(VolumeParameter))` と `BaseType.GetGenericArguments()` を確認する。
  - 観測可能な完了条件: URP 主要 9 種（Bool/Int/ClampedInt/Float/ClampedFloat/Color/Vector2/Vector3/Vector4 のいずれかを field に持つ Bloom/Tonemapping/ColorAdjustments）の各 field を Resolve すると正しい `ParamKind` が返ることが緑になる。
  - _Requirements: 5.1, 5.2_
  - _Boundary: Volume/VolumeParameterKindResolver_
  - _Depends: 1.1_

- [x] 5.3 VolumeOverrideMetadataBuilder の実装
  - `VolumeOverrideMetadataBuilder` を実装し、`Build(IReadOnlyList<Type> volumeComponentTypes)` で各型について以下を構築：
    - `TypeFullName = type.FullName`
    - `DisplayName`：`[VolumeComponentMenu]` 属性があれば末尾セグメント（`/` 区切り）を採用、無ければ型短名
    - `Params`：`type.GetFields(BindingFlags.Public | BindingFlags.Instance)` で `VolumeParameter` 派生 field を列挙
      - `ParamName = field.Name`
      - `Kind = VolumeParameterKindResolver.Resolve(field.FieldType)`
      - `DisplayName = field.Name`（暫定、`[InspectorName]` 属性等の対応は実装フェーズの `research.md` で確認）
      - `DefaultValue`：型のデフォルトインスタンスを `ScriptableObject.CreateInstance(type)` で生成し当該 field の `value` を採取して `VolumeOverrideParamValueDto` に格納
      - `Range`：`ClampedFloatParameter` / `MinFloatParameter` / `MaxFloatParameter` 等から min/max を抽出（リフレクションで `min` / `max` field を読む）、Enum なら `EnumValues = Enum.GetNames(enumType)`
    - 1 型の構築失敗時は警告ログ + その型をスキップ、残りは続行（Requirement 7.5）
  - `SchemaVersion = 1` で `VolumeOverrideSchemaDto` を返す。
  - 観測可能な完了条件: URP 標準 `Bloom` / `Tonemapping` / `ColorAdjustments` を入力すると、それぞれの主要 param（`Bloom.intensity` / `Tonemapping.mode` / `ColorAdjustments.colorFilter`）が期待の `ParamKind` で含まれる `VolumeOverrideSchemaDto` が返ることが PlayMode テストで確認できる。
  - _Requirements: 5.1, 5.2, 7.5_
  - _Boundary: Volume/VolumeOverrideMetadataBuilder_
  - _Depends: 5.2, 1.4_

- [x] 5.4 VolumeParameterReflectionSetter の実装
  - `VolumeParameterReflectionSetter.ApplyValue(VolumeComponent component, string paramName, VolumeOverrideParamValueDto value, AdapterLogger logger)` を実装する。
  - リフレクション手順：
    1. `field = component.GetType().GetField(paramName, BindingFlags.Public | BindingFlags.Instance)`、null なら警告ログ + false return（Requirement 5.6）
    2. `param = (VolumeParameter)field.GetValue(component)`
    3. `paramType = param.GetType()`
    4. `value.Kind` に応じて型変換した値を `paramType.GetProperty("value").SetValue(param, converted)` で代入
       - `Bool` → `value.BoolValue!.Value`
       - `Int` → `value.IntValue!.Value`
       - `Float` / `ClampedFloat` → `value.FloatValue!.Value`
       - `Color` → `new Color(value.ColorValue!.Value.R, ..., A)`
       - `Vector2/3/4` → `new Vector2/3/4(value.VectorValue!.Value.X, ..., W)`
       - `Enum` → `enumType = paramType.BaseType?.GetGenericArguments().First()`, `Enum.Parse(enumType, value.EnumValue!)`
    5. `param.overrideState = true` をリフレクションで設定（`typeof(VolumeParameter).GetField("overrideState")`）
  - 全 try/catch で囲み、失敗時は警告ログ + false return（Requirement 5.7, 5.9）。
  - 観測可能な完了条件: PlayMode で実 `Bloom` インスタンスを作成し、`intensity`（Float）/ `tint`（Color）/ `dirtIntensity`（Float）に対して `ApplyValue` で代入後、対応 `VolumeParameter<T>.value` と `overrideState=true` が反映されることが緑になる。
  - _Requirements: 5.4, 5.6, 5.7, 5.8, 5.9_
  - _Boundary: Volume/VolumeParameterReflectionSetter_
  - _Depends: 1.3, 1.4_

- [x] 5.5 VolumeOverrideHandler の実装
  - `VolumeOverrideHandler` を実装し、コンストラクタで `IOutputCommandDispatcher`, `IOutputSceneRoots`, `AdapterErrorReporter`, `AdapterLogger`, `StageLightingVolumeOutputAdapterDiagnostics` を受け取る。
  - `Start()` で以下を実行：
    1. `VolumeOverrideRegistry.Build(VolumeManager.instance.baseComponentTypeArray)` でレジストリ構築
    2. `VolumeOverrideMetadataBuilder.Build(...)` で `VolumeOverrideSchemaDto` を構築・キャッシュ
    3. 全 `VolumeComponent` 型について `RegisterStateHandler<bool>(StageLightingTopics.VolumeOverrideEnabled(typeFullName), OnEnabled)` を登録
    4. 全 `(typeFullName, paramName)` について `RegisterStateHandler<VolumeOverrideParamValueDto>(StageLightingTopics.VolumeOverrideParam(typeFullName, paramName), OnParam)` を登録（topic ワイルドカードが上流で対応されていれば `volume/override/+/+` で 1 本化）
    5. `RegisterRequestHandler<EmptyDto, VolumeOverrideSchemaDto>(StageLightingTopics.VolumeOverrideSchema, _ => cachedSchema)` を登録
    6. `Diagnostics.VolumeOverrideTypeCount` を更新
  - `OnEnabled(typeFullName, StateCommand<bool> cmd)`：`Registry.GetTypeByFullName(typeFullName, out type)` → `roots.GlobalVolumeProfile.TryGet(type, out component)`（false なら `roots.GlobalVolumeProfile.Add(type, overrideState: true)` で追加してから取得）→ `component.active = cmd.Payload`。
  - `OnParam(typeFullName, paramName, StateCommand<VolumeOverrideParamValueDto> cmd)`：`Registry.GetTypeByFullName` → `TryGet` または `Add` → `VolumeParameterReflectionSetter.ApplyValue(component, paramName, cmd.Payload, logger)`。
  - 全ハンドラを try/catch で囲み、未知 typeFullName は警告ログ + 破棄（Requirement 5.5）、未知 paramName / Kind 不整合は Setter 内で処理。
  - `Dispose()` で全登録解除トークン Dispose、`roots.GlobalVolumeProfile.components` から本 spec で追加した型を `Remove<T>` で除去（Requirement 5.10）。
  - 観測可能な完了条件: PlayMode で実 GlobalVolumeProfile に対し、`volume/override/UnityEngine.Rendering.Universal.Bloom/enabled = true` 受信で Bloom が profile に追加される、`volume/override/UnityEngine.Rendering.Universal.Bloom/intensity = 1.5f` 受信で `bloom.intensity.value == 1.5f && overrideState == true` になる、metadata request で 30+ 種が返る、PlayMode 終了時に components が空に戻る、の各シナリオが緑になる。
  - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 5.7, 5.9, 5.10, 7.1_
  - _Boundary: Volume/VolumeOverrideHandler_
  - _Depends: 2.1, 5.1, 5.3, 5.4_

## 6. Preview Domain

- [x] 6.1 PreviewRenderTextureFactory の実装
  - `PreviewRenderTextureFactory.Create(int width = 1280, int height = 720, RenderTextureFormat format = RenderTextureFormat.ARGB32)` で `RenderTexture` を新規生成し、`name = "PreviewRT"` 設定、`Create()` 呼出で GPU メモリ確保まで行う。
  - `Release(RenderTexture? rt)` で `rt.Release()` + `Object.Destroy(rt)` を null 安全に実行する。
  - 観測可能な完了条件: PlayMode で Create 後 `IsCreated() == true` / Release 後 nullable 化が確認できる。
  - _Requirements: 6.3_
  - _Boundary: Preview/PreviewRenderTextureFactory_
  - _Depends: 1.1_

- [x] 6.2 StagePreviewHost MonoBehaviour の実装
  - `StagePreviewHost : MonoBehaviour, IPreviewHostService` を実装し、private field で `RenderTexture? _rt`, `Camera? _previewCamera`, `SceneViewStyleCameraController? _cameraController` を保持する。
  - `IPreviewHostService` 実装：`CurrentRenderTexture => _rt`, `IsReady`, `event RenderTextureChanged`。
  - `Awake()`：try/catch で囲み、`_rt = PreviewRenderTextureFactory.Create()` → 同 GameObject の `Camera` / `SceneViewStyleCameraController` 参照取得 → `_previewCamera.targetTexture = _rt` → `StagePreviewHostLocator.Register(this)` → `IsReady = true` → `RenderTextureChanged?.Invoke(_rt)` 発火。失敗時は `IsReady = false`, Locator Register せず, 警告ログ（Requirement 7.6）。
  - `OnDestroy()`：`StagePreviewHostLocator.Unregister(this)` → `RenderTextureChanged?.Invoke(null)` → `PreviewRenderTextureFactory.Release(_rt)` → `_rt = null` → `IsReady = false`（Requirement 6.5, 6.14, 7.6）。
  - public メソッド：
    - `void SetEnabled(bool enabled)` → `_previewCamera.enabled = enabled`（Requirement 6.8, 6.9）
    - `void ResetView()` → `_cameraController?.ResetView()`（メソッド名は実装フェーズで `SceneViewStyleCameraController` v1.0.1 を確認、適合しない場合は Transform 直接操作にフォールバック、Requirement 6.10）
    - `void SyncCullingMaskFromDefault(Camera defaultCam)` → `_previewCamera.cullingMask = defaultCam.cullingMask`（Requirement 6.4）
    - `internal void DestroySafely()` → `Object.Destroy(this.gameObject)`
  - 観測可能な完了条件: PlayMode で Awake 後 `IsReady=true` + Locator.Current=this + RenderTextureChanged 発火、OnDestroy 後 Locator.Current=null + RenderTextureChanged(null) 発火 + RT 解放、SetEnabled で `Camera.enabled` が変化、が緑になる。
  - _Requirements: 6.1, 6.3, 6.5, 6.6, 6.7, 6.8, 6.9, 6.10, 6.14, 7.6_
  - _Boundary: Preview/StagePreviewHost_
  - _Depends: 1.4, 6.1_

- [x] 6.3 PreviewCameraFactory の実装
  - `PreviewCameraFactory.Build(IOutputSceneRoots roots) → StagePreviewHost` を実装し、以下を順次実行：
    1. `var go = new GameObject("PreviewCamera")`
    2. `go.transform.SetParent(roots.Cameras, worldPositionStays: false)`
    3. `var cam = go.AddComponent<UnityEngine.Camera>()` → `cam.cullingMask = roots.DefaultCamera.cullingMask`（Requirement 6.4）→ `cam.targetDisplay = 0`
    4. `var urpData = go.AddComponent<UniversalAdditionalCameraData>()` → `urpData.renderType = CameraRenderType.Base`（Requirement 6.15）
    5. `var ctrl = go.AddComponent<SceneViewStyleCameraController>()`（実 API は v1.0.1 を確認）
    6. `var host = go.AddComponent<StagePreviewHost>()`（host.Awake が同フレームで自動実行され RT 生成 + Locator Register が走る）
    7. `return host`
  - 観測可能な完了条件: 戻り値 `StagePreviewHost` の `IsReady == true`、`go.transform.parent == roots.Cameras`、`cam.cullingMask == roots.DefaultCamera.cullingMask` が緑になる。
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.15_
  - _Boundary: Preview/PreviewCameraFactory_
  - _Depends: 6.2_

- [x] 6.4 PreviewCommandHandler の実装
  - `PreviewCommandHandler` を実装し、コンストラクタで `IOutputCommandDispatcher`, `StagePreviewHost`, `AdapterLogger`, `StageLightingVolumeOutputAdapterDiagnostics` を受け取る。
  - `Start()` で以下を実行：
    1. `RegisterEventHandler<PreviewCommandDto>(StageLightingTopics.PreviewCommand, OnPreviewCommand)` 登録
    2. `PublishPreviewState()` で初期 `preview/state` publish
  - `OnPreviewCommand(EventCommand<PreviewCommandDto> cmd)`：`cmd.Payload.Op` で分岐：
    - `"set-enabled"` → `_host.SetEnabled(cmd.Payload.Enabled ?? true)`
    - `"reset-view"` → `_host.ResetView()`
    - `"init"` → `_host.SetEnabled(true)`
    - `"dispose"` → `_host.SetEnabled(false)`
  - 各分岐後に `PublishPreviewState()` を呼び `preview/state` を最新値で更新（Requirement 6.12）。
  - `PublishPreviewState()` → `PublishState(StageLightingTopics.PreviewState, new PreviewStateDto(Enabled: _host.PreviewCamera?.enabled ?? false, HostReady: _host.IsReady))`
  - `_host.RenderTextureChanged` 購読で RT 再生成検知時にも `PublishPreviewState()` を呼ぶ（HostReady 状態反映）。
  - 全ハンドラ try/catch + 例外時は警告ログのみ（Requirement 7.1）。
  - `Dispose()` で登録解除トークン Dispose + `RenderTextureChanged` 購読解除。
  - 観測可能な完了条件: `FakeOutputCommandDispatcher` 経由で 4 op を inject すると `_host.SetEnabled` / `ResetView` 呼出と `preview/state` publish が緑になる。
  - _Requirements: 6.8, 6.9, 6.10, 6.11, 6.12, 7.1_
  - _Boundary: Preview/PreviewCommandHandler_
  - _Depends: 1.4, 6.2_

## 7. Composition Root と起動統合

- [x] 7.1 StageLightingVolumeOutputAdapterBootstrapper の実装
  - `StageLightingVolumeOutputAdapterBootstrapper : MonoBehaviour` を実装し、`SerializeField bool _autoStart = true` および全 Handler の private field を保持する。
  - `Awake()`：`if (!Application.isPlaying) return;` で Edit モード非常駐を担保（Requirement 8.5）。`ResolveDependencies()` で `IOutputCommandDispatcher` と `IOutputSceneRoots` を `OutputSceneBootstrapper` 経由で取得（実 API は実装フェーズで確認、`FindObjectOfType<OutputSceneBootstrapper>()?.GetService<IOutputCommandDispatcher>()` 等の Service Locator 経由）。
  - `Start()`：依存解決済みなら `AdapterLogger`, `AdapterErrorReporter`, `Diagnostics` を構築 → 各 Handler を構築 → `PreviewCameraFactory.Build(_roots)` で `StagePreviewHost` Instantiate → `PreviewCommandHandler` 構築 → 各 Handler の `Start()` 順次呼出 → `Diagnostics.SetReady(true)`。依存未解決時は警告ログ + 早期 return（Requirement 2.8）。
  - `OnDestroy()`：`if (!Application.isPlaying) return;` ガード後、逆順で `_preview?.Dispose()` → `_previewHost?.DestroySafely()` → `_volume?.Dispose()` → `_light?.Dispose()` → `_stage?.Dispose()` を実行。各 Dispose は try/catch で囲み、失敗しても次の Dispose を続行する（Requirement 8.3, 8.4）。
  - public プロパティ `Diagnostics : StageLightingVolumeOutputAdapterDiagnostics?` を公開（外部から診断スナップショット取得用、Requirement 9.5）。
  - 観測可能な完了条件: PlayMode 開始で `IsReady=true` / 登録ハンドラ数が期待数 / `StagePreviewHostLocator.Current=this`、PlayMode 停止で全 Dispose 経路が緑、Edit モードで Awake/Start が早期 return することが緑になる。
  - _Requirements: 1.1, 2.1, 2.2, 2.3, 2.5, 2.6, 2.7, 2.8, 8.1, 8.2, 8.3, 8.5, 8.6, 8.7, 9.5_
  - _Boundary: Bootstrap/StageLightingVolumeOutputAdapterBootstrapper_
  - _Depends: 3.4, 4.4, 5.5, 6.3, 6.4, 1.5_

- [x] 7.2 AdapterStartupRegistration とシーン組み込み手順
  - `OutputSceneBootstrapper` の Init 完了タイミング（`OutputSceneInitPhase.Complete`）に応じて本 Bootstrapper を起動するための補助コードを実装する。`OutputSceneBootstrapper` 側に明示的な拡張点（`OnInitComplete` event 等）が無い場合は、本 Bootstrapper の `Start()` 内で `IOutputDiagnostics.CurrentPhase == Complete` をポーリング待機（最大 60 フレーム）するフォールバック実装を `AdapterStartupRegistration` ヘルパで提供する。
  - 利用者プロジェクトでの組み込み手順（Bootstrapper を `OutputSceneBootstrapper` のシーンに `AddComponent` する 1 ステップ）を README に記載する。
  - 観測可能な完了条件: PlayMode テストで `OutputSceneBootstrapper` の `OutputSceneInitPhase.Complete` 観測後に本 Bootstrapper の `Start()` ロジックが走ることが緑になる。
  - _Requirements: 2.1, 8.1, 8.2_
  - _Boundary: Bootstrap/AdapterStartupRegistration_
  - _Depends: 7.1_

## 8. Failure Handling & Observability Integration

- [x] 8.1 Stage / Light / Volume / Preview の例外捕捉パスの結線確認
  - 各 Handler の全 public メソッド（IPC ハンドラ + Start/Dispose）を try/catch で囲み、`AdapterLogger.Error` で診断ログを記録、必要に応じて `AdapterErrorReporter` 経由でエラー event を publish する経路を仕上げる（Requirement 7.1, 7.2, 7.3, 7.4）。
  - メイン出力サーフェス（Display 2+）への OnGUI / IMGUI 描画経路を持たないことを構造的にレビューし、テストで `EditorApplication.isPlaying` 中に `GameObject.Find` 等で UI レイヤー混入が無いことを確認する（Requirement 7.8）。
  - 構造化診断ログのフォーマット（topic, lightId, typeFullName, paramName, exception）を全 Handler で統一する（Requirement 7.9, 9.2）。
  - 観測可能な完了条件: 各 Handler の全 IPC ハンドラに対して例外を inject すると、`AdapterLogger.Error` 記録 + 描画継続 + 必要なエラー event publish が緑になる。
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.8, 7.9, 9.2_
  - _Boundary: Diagnostics/AdapterErrorReporter, 全 Handler_
  - _Depends: 3.4, 4.4, 5.5, 6.4_

- [ ] 8.2 PlayMode 反復 5 回ゼロリーク検証テスト
  - `BootstrapPlayModeTests.PlayModeRepeats5TimesNoLeak` を実装し、`OutputSceneBootstrapper` シーン → 本 Bootstrapper Activate → PlayMode 終了 → 再起動を 5 回繰り返した後に以下を検査する：
    - `Resources.FindObjectsOfTypeAll<UnityEngine.Light>().Where(l => l.gameObject.name.StartsWith("Light_")).Count() == 0`
    - `Resources.FindObjectsOfTypeAll<StagePreviewHost>().Length == 0`
    - `StagePreviewHostLocator.Current == null`
    - `Addressables.PrintDiagnostics()` の統計でアダプタ起動分のリークが 0
    - `_roots.GlobalVolumeProfile.components` 内に本 spec で追加した `VolumeComponent` が残っていない
  - _Requirements: 2.5, 8.4, 10.7_
  - _Boundary: Tests/PlayMode/BootstrapPlayModeTests_
  - _Depends: 7.1_

- [ ] 8.3 診断 API と観測性ログの整備
  - `StageLightingVolumeOutputAdapterDiagnostics.Capture()` が副作用なくスナップショットを生成することを確認するテストを追加する。
  - design.md「Monitoring & Observability」で列挙された主要イベント（`Stage.SwapStarted`, `Stage.SwapCompleted`, `Stage.SwapFailed`, `Light.Added`, `Light.Removed`, `Volume.OverrideEnabled`, `Volume.ParamApplied`, `Preview.Enabled`, `Preview.Disabled`, `Preview.HostRegistered`, `Preview.HostUnregistered`, `Adapter.HandlerRegistered`, `Adapter.HandlerDisposed`, `Adapter.ErrorReported`）を `AdapterLogger` 経由で記録する経路を全 Handler に結線する。
  - ログレベル外部切替が `AdapterLoggerConfig.MinLevel` で機能することを統合テストで確認する。
  - 観測可能な完了条件: 主要 5 シナリオ（初期化・Stage 切替・Light add/remove・Volume Override 反映・Preview enable/disable）でログが期待カテゴリで記録され、`Capture()` が正しい値を返すテストが緑になる。
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_
  - _Boundary: Diagnostics/AdapterLogger, StageLightingVolumeOutputAdapterDiagnostics_
  - _Depends: 8.1_

## 9. 単体検証と回帰テスト

- [ ] 9.1 EditMode 統合テスト：Stage / Light / Volume / Preview のラウンドトリップ
  - `FakeOutputCommandDispatcher` + `FakeOutputSceneRoots` + `FakeInstantiationProvider` を組み合わせて以下のシナリオを 1 セッションで検証する EditMode 統合テストを実装する：
    1. Bootstrapper Start → 各 Handler の登録ハンドラ数を `Diagnostics.RegisteredHandlerCount` で確認
    2. `EmitState(stage/active, new StageCommandDto("load", "TestStage"))` → `FakeInstantiationProvider` で成功応答 → `PublishedStates` に `stage/current` が含まれることを確認
    3. `EmitEvent(light/command, new LightCommandDto("add", null, initial))` → lightId 採番 → `PublishedEvents` に `light/added`、`PublishedStates` に `lights/list` が含まれることを確認
    4. `EmitState(light/{lightId}/intensity, 2.5f)` → `LightRegistry` 内の `Light.intensity == 2.5f` を確認
    5. `EmitState(volume/override/UnityEngine.Rendering.Universal.Bloom/enabled, true)` → 実 GlobalVolumeProfile に Bloom が追加されていることを確認
    6. `InvokeRequest(volume/overrides/metadata, EmptyDto)` → `VolumeOverrideSchemaDto` が返り、`Types.Count > 0` を確認
    7. `EmitEvent(preview/command, new PreviewCommandDto("set-enabled", true))` → `_host.PreviewCamera.enabled == true` を確認
  - _Requirements: 10.1, 10.2, 10.3, 10.4_
  - _Boundary: Tests/Editor/Integration_
  - _Depends: 7.1, 8.1_

- [ ] 9.2 PlayMode 統合テスト：実 Addressables / 実 URP / 実 RenderTexture
  - `StageHandlerPlayModeTests` を実装し、サンプル Stage Prefab を Addressables Group に登録、本 Bootstrapper を Active な実シーンで起動、`stage/active` 受信で実 GameObject が `StageRoot` 配下に Instantiate されることを確認する。`stage/active` を別キーで再受信すると旧ステージが ReleaseInstance され新ステージが配置される lazy swap を確認する。
  - `VolumeOverrideHandlerPlayModeTests` を実装し、実 Camera + 実 GlobalVolumeProfile + Bloom を使ってスクリーンショット比較ではなく `bloom.intensity.value == 1.5f && bloom.intensity.overrideState == true` の状態確認を行う（Requirement 10.2）。
  - `StagePreviewHostPlayModeTests` を実装し、Awake → Locator Register → IsReady=true、OnDestroy → Locator Unregister → RenderTextureChanged(null) → RT 解放のラウンドトリップを実 GameObject で確認する（Requirement 10.5）。
  - _Requirements: 10.2, 10.3, 10.5_
  - _Boundary: Tests/PlayMode/*_
  - _Depends: 7.1_

- [ ] 9.3 PlayMode サンプルシーンと手動検証手順
  - `StageLightingVolumeOutputAdapterPlayModeSample.unity` を作成し、最小構成（`OutputSceneBootstrapper` + 本 Bootstrapper + 簡易な Mock IPC ドライバ）で全機能を確認できるシーンを提供する。
  - シーン Inspector に「Light を追加」「Bloom を有効化」「Stage を切替（Addressables 必須）」「Preview を on/off」の各ボタンを持つ MonoBehaviour（Editor only）を配置し、`FakeOutputCommandDispatcher` 経由で IPC を inject できる構成にする。
  - README に手動検証手順（PlayMode 開始 → 各ボタン操作 → 期待結果の観察項目）を整備する。
  - 観測可能な完了条件: PlayMode 起動でメイン出力に既定シーン（DefaultCamera + DefaultLight + 空 Volume）が表示され、シーンボタン操作で Light 追加・Bloom 有効化・Stage 切替の効果がメイン出力カメラに反映され、PreviewCamera の RenderTexture が Inspector の Preview パネルで確認できる。
  - _Requirements: 10.6_
  - _Boundary: Tests/PlayMode/StageLightingVolumeOutputAdapterPlayModeSample_
  - _Depends: 7.1, 9.2_

- [ ] 9.4* パフォーマンス / 負荷検証（任意）
  - Light 32 個追加 → 各 Light の intensity を 60Hz × 5 秒で連続更新するシナリオで `Time.unscaledDeltaTime` が 16.67 ms を維持することを計測する。
  - Bloom + Tonemapping + ColorAdjustments の 3 Override 全 param を 60Hz × 5 秒で連続更新する同シナリオでも 16.67 ms を維持することを計測する。
  - Stage 切替を 100 回連続で実行し、`Addressables.PrintDiagnostics` で起動時に対する追加リークが 0 であることを計測する。
  - 観測可能な完了条件: 計測レポートが 3 指標を記録し、しきい値を満たすか未達かを判定可能にする。
  - _Requirements: 7.8, 8.7_
  - _Boundary: Tests/PlayMode/PerformanceTests_
  - _Depends: 9.3_

- [ ] 9.5* IL2CPP スタンドアロンビルドでの link.xml 検証手順（任意）
  - スタンドアロン IL2CPP ビルドを行い、`Bloom.intensity` のリフレクション代入が strip されていないことを確認する手動検証手順を README に整備する。
  - 利用者プロジェクト独自 `VolumeComponent` の strip 抑止のための `link.xml` 追加例を提供する。
  - _Requirements: 5.8, 10.8_
  - _Boundary: README, Samples~/IntegratedDemo_
  - _Depends: 1.2, 9.3_
