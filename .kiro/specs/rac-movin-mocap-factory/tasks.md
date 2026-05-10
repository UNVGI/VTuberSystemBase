# Implementation Plan

本タスク群は `rac-movin-mocap-factory` パッケージを新規追加し、`rac-main-output-adapter` 側に DI seam (`IMoCapSourceConfigFactoryProvider`) を追加して `IntegratedDemoBootstrap` から自動配線するまでを、`/kiro:spec-run` が `codex exec` で無人実行できる粒度に分解したものです。各タスクは設計書 (`design.md`) の File Structure Plan に従い、ファイルパスレベルの受け入れ基準と参照すべき Requirements / Design セクションを明記しています。

実行順序の原則:
- タスク 1（パッケージ骨格）は他のすべてのタスクの前提となる Foundation です。
- タスク 2（契約インタフェース追加）は `rac-main-output-adapter` 側の新規型のみを追加するため、タスク 3 / 4 と並列実行可能ですが、タスク 5（Host 改修）はこの契約に依存するため逐次実行となります。
- テスト実行系タスクは実装が一通り終わった後の最終検証フェーズに配置しています。

## Foundation Phase

- [ ] 1. パッケージ骨格を作成し Unity がローカルパッケージとして検出できる状態にする
- [ ] 1.1 `rac-movin-mocap-factory` パッケージのディレクトリと `package.json` を新規作成する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/` ディレクトリを作成する
  - `package.json` を新規作成し、`name = "jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory"`、`version = "0.1.0"`、`displayName`、`description`、`unity = "6000.3"`、`dependencies` に `jp.co.unvgi.vtuber-system-base.rac-main-output-adapter: 0.1.0` と `jp.co.unvgi.realtimeavatarcontroller.movin: 0.1.7` を含める
  - `package.json.meta` をランダム 32-hex GUID（global CLAUDE.md 規定）で生成する
  - 観測可能な完了条件: Unity Package Manager がエラーなく当該パッケージを表示し、依存解決が成功する状態
  - _Requirements: 1.1, 1.2, 1.3_

- [ ] 1.2 Runtime asmdef と補助ファイル（AssemblyInfo / IsExternalInit）を配置する
  - `Runtime/jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory.asmdef` を作成し、`name = "jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory"`、`references` に `VTuberSystemBase.RacMainOutputAdapter.Runtime` / `RealtimeAvatarController.Core` / `RealtimeAvatarController.MoCap.Movin` を含める（autoReferenced: true、includePlatforms / excludePlatforms 空、allowUnsafeCode: false）
  - `Runtime/AssemblyInfo.cs` に `InternalsVisibleTo("jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory.tests")` を記述
  - `Runtime/IsExternalInit.cs` に C# 9 init-only プロパティ用 shim（`namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }`）を記述
  - 各 `.cs` および `.asmdef` の `.meta` をランダム 32-hex GUID で生成し、`Runtime/.meta` も生成する
  - 観測可能な完了条件: Unity が `jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory` という Runtime アセンブリを生成しコンパイル成功する状態
  - _Requirements: 1.4_

- [ ] 1.3 Tests/EditMode asmdef を配置する
  - `Tests/EditMode/jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory.tests.asmdef` を作成
  - `name = "jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory.tests"`、`references` に `jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory`、`VTuberSystemBase.RacMainOutputAdapter.Runtime`、`RealtimeAvatarController.Core`、`RealtimeAvatarController.MoCap.Movin` を列挙
  - `optionalUnityReferences` に `TestAssemblies`、`precompiledReferences` に `nunit.framework.dll`、`includePlatforms` を `Editor` のみに限定、`defineConstraints` に `UNITY_INCLUDE_TESTS` を設定
  - `Tests/.meta`、`Tests/EditMode/.meta`、asmdef `.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: Unity Test Runner の EditMode タブにこのアセンブリが空テストアセンブリとして列挙される状態
  - _Requirements: 1.4, 9.7_

## Contract Phase

- [ ] 2. `rac-main-output-adapter` に `IMoCapSourceConfigFactoryProvider` 契約インタフェースを追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/ExtensionPoints/IMoCapSourceConfigFactoryProvider.cs` を新規作成する
  - 名前空間は `VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints` とし、`IMoCapSourceConfigFactory Factory { get; }` プロパティのみを公開する（design.md `IMoCapSourceConfigFactoryProvider` Service Interface 参照）
  - クラスドキュメントコメントに「`Factory` は未初期化時に null を返してよく、Host 側はその場合 Stub フォールバックする」旨を明記
  - `IMoCapSourceConfigFactoryProvider.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: `rac-main-output-adapter` Runtime アセンブリのコンパイルが通り、新インタフェースを他パッケージから参照可能になる状態
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

## Core Phase

- [ ] 3. (P) `MovinMoCapSourceConfigFactory` POCO 実装を追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/Runtime/MovinMoCapSourceConfigFactory.cs` を新規作成
  - 名前空間は `VTuberSystemBase.RacMovinMoCapFactory`、`sealed class` として `IMoCapSourceConfigFactory` を実装（design.md `MovinMoCapSourceConfigFactory` Service Interface のスニペット準拠）
  - コンストラクタで `port` (default 11235) / `rootBoneName` (default "") / `boneClass` (default "") を受け取り、null 文字列は `string.Empty` に正規化して保持
  - `Build(string slotId)` 内で `ScriptableObject.CreateInstance<MovinMoCapSourceConfig>()` を毎回新規生成し、`name = $"MovinMoCapSourceConfig_{slotId}"`、`port` / `rootBoneName` / `boneClass` を代入し、`MoCapSourceDescriptor { SourceTypeId = MovinMoCapSourceFactory.MovinSourceTypeId, Config = config }` を返す
  - `MovinMoCapSource` のインスタンス化や uOSC bind は行わない（Slot Active 遷移時の責務）
  - `.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: 当該クラスがコンパイル通過し、`new MovinMoCapSourceConfigFactory().Build("slot-A")` が `SourceTypeId == "MOVIN"` の Descriptor を返せる状態
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 8.2, 8.4_
  - _Boundary: rac-movin-mocap-factory/Runtime/MovinMoCapSourceConfigFactory_
  - _Depends: 1.2, 2_

- [ ] 4. (P) `MovinMoCapSourceConfigFactoryProvider` MonoBehaviour を追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/Runtime/MovinMoCapSourceConfigFactoryProvider.cs` を新規作成
  - 名前空間は `VTuberSystemBase.RacMovinMoCapFactory`、`sealed class : MonoBehaviour, IMoCapSourceConfigFactoryProvider`
  - `[DisallowMultipleComponent]` および `[AddComponentMenu("VTuberSystemBase/RAC MOVIN MoCap Factory Provider")]` を付与
  - `[SerializeField, Range(1, 65535)] private int port = 11235;`、`[SerializeField] private string rootBoneName = "";`、`[SerializeField] private string boneClass = "";` を Inspector 上に提示（Tooltip は design.md 準拠）
  - `public IMoCapSourceConfigFactory Factory => new MovinMoCapSourceConfigFactory(port, rootBoneName, boneClass);` で都度新インスタンスを返し、Awake/Start で MOVIN 実体を生成しない（Edit モード副作用ゼロ）
  - `.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: 当該クラスがコンパイル通過し、新規 GameObject に `AddComponent` した Provider の `Factory` プロパティが Inspector 値を反映した Factory を返せる状態
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 8.1_
  - _Boundary: rac-movin-mocap-factory/Runtime/MovinMoCapSourceConfigFactoryProvider_
  - _Depends: 1.2, 2, 3_

## Integration Phase

- [ ] 5. `RacMainOutputAdapterHost` に MoCap Factory Provider 用 DI seam を追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/Bootstrapper/RacMainOutputAdapterHost.cs` を編集
  - `[Header("MoCap Factory Provider (optional)")]` ブロックを追加し、`[Tooltip("...")]` 付きの `[SerializeField] private MonoBehaviour _mocapFactoryProviderBehaviour;` を新設（既存 `_coreIpcBusProviderBehaviour` の直後など、対称性のある位置に配置）
  - `Start()` 内、既存の `_bootstrapper.OverrideServices(dispatcher, sceneRoots, messageSink, logger)` 呼出の **直後**、`_bootstrapper.Initialize()` の **直前** に design.md 「Modification Detail」スニペット相当のロジックを追加（`_mocapFactoryProviderBehaviour is IMoCapSourceConfigFactoryProvider mocapProvider` で型チェック → `provider.Factory` 取得 → 非 null なら `OverrideServices(mocapFactory: ...)` を呼び `Debug.Log`、null なら `Debug.LogWarning` のみで Stub フォールバック）
  - 既存 `_coreIpcBusProviderBehaviour` / `_outputSceneBootstrapper` / `OverrideMessageSink` 周辺のコードは変更しない
  - `Application.isPlaying == false` の Edit モードでは Provider を参照しない（既存 `Awake`/`Start` の `Application.isPlaying` ガードに従う）
  - 観測可能な完了条件: `rac-main-output-adapter` Runtime アセンブリがコンパイル成功し、Inspector 上の `RacMainOutputAdapterHost` に新 SerializeField スロットが表示され、Provider 未設定時は従来どおり Stub フォールバック挙動になる状態
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 7.1, 8.3_
  - _Depends: 2_

- [ ] 6. `IntegratedDemoBootstrap` に MOVIN Provider の自動配線処理を追加する
- [ ] 6.1 `integrated-demo/package.json` の `dependencies` に新パッケージを追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.integrated-demo/package.json` を編集し、`dependencies` に `"jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory": "0.1.0"` を追加
  - 既存 dependencies の並び順・フォーマットを維持
  - 観測可能な完了条件: Unity Package Manager の `integrated-demo` 詳細ペインに新依存が表示され、依存解決エラーが出ない状態
  - _Requirements: 1.3, 6.1_
  - _Depends: 1.1_

- [ ] 6.2 `IntegratedDemoBootstrap.cs` に Provider AddComponent + reflection 注入処理を追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.integrated-demo/Runtime/IntegratedDemoBootstrap.cs` を編集
  - `EnsureMainOutputAdapters()` 内、`BindBusProviderToRacHostViaReflection(_racHost);` の **直後**、`_stageHost = ...` の **直前** に MOVIN Provider 配線スニペット（design.md 「Modification Detail」準拠）を挿入し、`GetComponent<MovinMoCapSourceConfigFactoryProvider>() ?? gameObject.AddComponent<...>()` パターンで Provider を確保
  - 新規 private method `BindMocapProviderToRacHostViaReflection(RacMainOutputAdapterHost host, MovinMoCapSourceConfigFactoryProvider provider)` を追加し、`typeof(RacMainOutputAdapterHost).GetField("_mocapFactoryProviderBehaviour", BindingFlags.Instance | BindingFlags.NonPublic)` で取得した FieldInfo に Provider を `SetValue` する
  - 例外発生時およびフィールド解決失敗時には `Debug.LogWarning` を出して継続（Stub フォールバックを阻害しない）
  - 既存の `_coreIpcBusProviderBehaviour` / `_outputSceneBootstrapper` 注入処理は変更しない
  - 観測可能な完了条件: `integrated-demo` Runtime アセンブリがコンパイル成功し、IntegratedDemo シーン起動時に Console に `MoCap factory provider resolved: MovinMoCapSourceConfigFactoryProvider` 相当のログが出力される状態
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  - _Depends: 4, 5, 6.1_

## Validation Phase

- [ ] 7. EditMode テストで Factory POCO の契約を検証する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/Tests/EditMode/MovinMoCapSourceConfigFactoryTests.cs` を新規作成
  - `Build_ReturnsDescriptorWithMovinSourceTypeId`: `new MovinMoCapSourceConfigFactory().Build("slot-A").SourceTypeId` が `MovinMoCapSourceFactory.MovinSourceTypeId`（"MOVIN"）と等しいこと
  - `Build_ReturnsDescriptorWithMovinConfig`: `Build("slot-A").Config is MovinMoCapSourceConfig` が真であること
  - `Build_AppliesPortRootBoneNameBoneClass`: ctor に `port=12345, rootBoneName="Hips", boneClass="Humanoid"` を渡したとき、`Build("slot-A").Config` の対応フィールドが期待値であること
  - `Build_ProducesDistinctConfigInstances`: `Build("slot-A")` を 2 回呼んだ結果の `Config` 参照が `Object.ReferenceEquals` で異なること
  - `Build_NameContainsSlotId`: `Build("slot-X").Config.name` に文字列 `"slot-X"` が含まれること
  - `Constructor_NormalizesNullStringsToEmpty`: ctor に `rootBoneName: null, boneClass: null` を渡したとき `RootBoneName` / `BoneClass` プロパティが `string.Empty` を返すこと
  - 各テスト後に生成した ScriptableObject を `Object.DestroyImmediate` で破棄して EditMode リーク防止
  - `.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: Unity Test Runner の EditMode タブで上記 6 テストが全て PASS する状態
  - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 9.1, 9.2, 9.5, 9.7_
  - _Depends: 1.3, 3_

- [ ] 8. EditMode テストで Provider MonoBehaviour の契約と値伝搬を検証する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/Tests/EditMode/MovinMoCapSourceConfigFactoryProviderTests.cs` を新規作成
  - `DefaultPortIsExpectedValue`: 新規 `GameObject` に `MovinMoCapSourceConfigFactoryProvider` を `AddComponent` し、reflection で `port` private field を読み 11235 と等しいこと
  - `Factory_PropagatesSerializedValues`: reflection で `port=12345`, `rootBoneName="Hips"`, `boneClass="Humanoid"` をセットし、`provider.Factory.Build("slot-A").Config` 上の `MovinMoCapSourceConfig` が期待値を持つこと
  - `Factory_ReturnsNonNullInstance`: AddComponent 直後の `provider.Factory != null` を確認
  - `Factory_ImplementsIMoCapSourceConfigFactoryProvider`: `provider is IMoCapSourceConfigFactoryProvider` を確認
  - `[SetUp]` で GameObject 生成、`[TearDown]` で `Object.DestroyImmediate` により破棄
  - `.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: Unity Test Runner の EditMode タブで上記 4 テストが全て PASS する状態
  - _Requirements: 3.1, 3.2, 3.5, 3.6, 4.3, 9.3, 9.4, 9.7_
  - _Depends: 1.3, 4_

- [ ]* 9. (Optional) `RacMainOutputAdapterBootstrapper.OverrideServices` 経由で MOVIN Factory が差し替わることを検証する統合 EditMode テストを追加する
  - `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/Tests/EditMode/MovinFactoryOverrideServicesIntegrationTests.cs` を新規作成（オプション扱い、MVP 後の追加検証として位置付ける）
  - `Bootstrapper_OverrideServices_AcceptsMovinFactory`: `RacMainOutputAdapterBootstrapper` を生成して必要なスタブ依存（dispatcher / messageSink / sceneRoots など）を `OverrideServices` で渡したうえで `OverrideServices(mocapFactory: new MovinMoCapSourceConfigFactory())` を呼び、`Initialize()` 後に内部 `_mocapFactory` が MOVIN 実装であることを reflection で観測する（または `SlotAssignmentApplier` 経由の `Build` 結果で観測）
  - スタブ依存生成が困難な場合は `[Ignore("MOVIN factory injection observed via Bootstrapper internals; revisit when stub dispatcher API is exposed")]` を付与しスキップ理由をコメントで明示
  - `.cs.meta` をランダム 32-hex GUID で生成
  - 観測可能な完了条件: Unity Test Runner の EditMode タブで本テストが PASS、または `[Ignore]` 状態で列挙される状態
  - _Requirements: 5.3, 7.1, 9.6_
  - _Depends: 1.3, 3, 5_

- [ ] 10. ビルド・テストの最終検証を実施する
  - Unity CLI またはエディタで以下を実行して結果を記録する:
    1. プロジェクト全体のコンパイル（`-batchmode -quit -projectPath VTuberSystemBase` などで型エラーがないこと）
    2. EditMode テスト実行: `jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory.tests` アセンブリの全 PASS
    3. 既存 `rac-main-output-adapter` / `integrated-demo` の EditMode テスト（存在する場合）が回帰していないこと
  - `Application.isPlaying == false` 状態でも `MovinMoCapSourceConfigFactoryProvider.Factory` 呼出が副作用ゼロであることを確認（テスト 8 で担保済みであることをログで再確認）
  - 既定 Stub フォールバックが Provider 未設定時に動作することを既存 EditMode テストの結果から確認（タスク 5 受入基準）
  - 観測可能な完了条件: 上記 1-3 が全て成功し、コンパイルログ / テストログがクリーンな状態
  - _Requirements: 1.5, 7.1, 7.2, 7.3, 7.4, 8.1, 8.2, 8.3, 8.4, 9.7_
  - _Depends: 7, 8_

## Requirements Coverage Map

| Requirement | Covered by Task(s) |
|-------------|--------------------|
| 1.1 | 1.1 |
| 1.2 | 1.1 |
| 1.3 | 1.1, 6.1 |
| 1.4 | 1.2, 1.3 |
| 1.5 | 5, 10 |
| 2.1 | 3 |
| 2.2 | 3, 7 |
| 2.3 | 3, 7 |
| 2.4 | 3, 7 |
| 2.5 | 3, 7 |
| 2.6 | 3, 7 |
| 2.7 | 3, 7 |
| 2.8 | 3, 7 |
| 3.1 | 4, 8 |
| 3.2 | 4, 8 |
| 3.3 | 4 |
| 3.4 | 4 |
| 3.5 | 4, 8 |
| 3.6 | 4, 8 |
| 3.7 | 4 |
| 4.1 | 2 |
| 4.2 | 2 |
| 4.3 | 2, 8 |
| 4.4 | 2 |
| 5.1 | 5 |
| 5.2 | 5 |
| 5.3 | 5, 9 |
| 5.4 | 5 |
| 5.5 | 5 |
| 5.6 | 5 |
| 6.1 | 6.1, 6.2 |
| 6.2 | 6.2 |
| 6.3 | 6.2 |
| 6.4 | 6.2 |
| 6.5 | 6.2 |
| 7.1 | 5, 9, 10 |
| 7.2 | 10 |
| 7.3 | 10 |
| 7.4 | 10 |
| 8.1 | 4, 10 |
| 8.2 | 3, 10 |
| 8.3 | 5, 10 |
| 8.4 | 3, 10 |
| 9.1 | 7 |
| 9.2 | 7 |
| 9.3 | 8 |
| 9.4 | 8 |
| 9.5 | 7 |
| 9.6 | 9 |
| 9.7 | 7, 8, 10 |

## Parallelism Notes

- タスク 3 と 4 は同一 Runtime asmdef 配下のため厳密には同一アセンブリビルドを共有しますが、編集対象ファイルが完全に分離している（`MovinMoCapSourceConfigFactory.cs` vs `MovinMoCapSourceConfigFactoryProvider.cs`）ため `(P)` を付与しました。`/kiro:spec-run` が並列実行する場合でも生成コミットの順序のみ整合させれば衝突しません。
- タスク 2（`rac-main-output-adapter` 側の契約追加）はタスク 3 / 4 / 5 の前提となるため `(P)` を付与せず逐次実行とします。
- タスク 5（Host 改修）はタスク 2 に依存し、また既存ファイル `RacMainOutputAdapterHost.cs` を編集するため、タスク 6.2 の reflection 配線とは編集対象パッケージが異なるものの依存方向（Host のフィールド名が安定してから `IntegratedDemoBootstrap` を編集）を尊重し逐次配置しています。
- タスク 6.1（integrated-demo/package.json 編集）はタスク 1.1（新パッケージ作成）の完了が必要であり、タスク 6.2 の前提となります。
- タスク 7 / 8 は対象テストファイルが分離しており、原理的には並列可能ですが、Unity の EditMode テスト実行は単一 TestRunner プロセスを共有するため、タスク順序として直列（7 → 8 → 10）で並べています。
