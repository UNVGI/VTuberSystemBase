# Requirements Document

## Project Description (Input)
rac-movin-mocap-factory: 新規ローカルパッケージ `com.hidano.vtuber-system-base.rac-movin-mocap-factory` を作成し、`MovinMoCapSourceConfigFactory : IMoCapSourceConfigFactory`（`MovinMoCapSourceConfig` を `ScriptableObject` として生成、`SourceTypeId="MOVIN"` で `MoCapSourceDescriptor` を返す）と Provider MonoBehaviour（port / rootBoneName / boneClass オーバーライド可）を提供する。`RacMainOutputAdapterHost` に `IMoCapSourceConfigFactoryProvider` を実装した MonoBehaviour を `SerializeField` で受け付ける seam を追加し、`bootstrapper.OverrideServices(mocapFactory: provider.Factory)` を呼ぶ。`IntegratedDemoBootstrap` に MOVIN factory provider を AddComponent + reflection で `RacMainOutputAdapterHost` に注入する処理を追加する。EditMode テストで `MovinMoCapSourceConfigFactory` が `SourceTypeId="MOVIN"` の `MoCapSourceDescriptor` を返すこと、`Config` が `MovinMoCapSourceConfig` 型であること、Provider が serialized port/boneClass を反映することを検証する。既存の `StubMoCapSourceConfigFactory` のフォールバックは破壊しない。本パッケージは `rac-main-output-adapter` と `realtimeavatarcontroller.movin` の両方に依存する。

## Introduction
本仕様は、RAC（RealtimeAvatarController）の MOVIN MoCap ソースを `rac-main-output-adapter` の Slot ライフサイクルに接続するための薄いブリッジパッケージ `com.hidano.vtuber-system-base.rac-movin-mocap-factory` を定義する。本パッケージは `IMoCapSourceConfigFactory` の MOVIN 実装と、Inspector から差し込み可能な Provider MonoBehaviour、ならびに `RacMainOutputAdapterHost` 側の DI seam（`IMoCapSourceConfigFactoryProvider`）を提供し、`IntegratedDemoBootstrap` から自動配線できるようにする。これにより、利用者プロジェクトは既定の `StubMoCapSourceConfigFactory` フォールバックを破壊することなく、Inspector ベースで MOVIN ソースを Slot に接続可能となる。

## Boundary Context
- **In scope**:
  - 新規ローカルパッケージ `com.hidano.vtuber-system-base.rac-movin-mocap-factory`（`name = "jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory"`）の追加。
  - `MovinMoCapSourceConfigFactory : IMoCapSourceConfigFactory` 実装の提供（`SourceTypeId="MOVIN"`、`Config = ScriptableObject.CreateInstance<MovinMoCapSourceConfig>()`、port / rootBoneName / boneClass オーバーライド適用）。
  - `MovinMoCapSourceConfigFactoryProvider : MonoBehaviour, IMoCapSourceConfigFactoryProvider` の提供（serialized port / rootBoneName / boneClass を持ち、設定済み Factory を `Factory` プロパティで返す）。
  - `IMoCapSourceConfigFactoryProvider` 契約インタフェースの提供（最終的な配置先は設計フェーズで確定するが、契約自体は本仕様で規定する）。
  - `RacMainOutputAdapterHost` への DI seam 追加：`[SerializeField] private MonoBehaviour _mocapFactoryProviderBehaviour;` を追加し、`Start()` 内で `Initialize()` 前に `bootstrapper.OverrideServices(mocapFactory: provider.Factory)` を呼ぶ。
  - `IntegratedDemoBootstrap` への自動配線：`EnsureMainOutputAdapters()` 内で MOVIN Provider を `RacMainOutputAdapterHost` と同一 GameObject に `AddComponent` し、reflection で `_mocapFactoryProviderBehaviour` に注入する。
  - 上記契約および挙動を検証する EditMode テスト群（新規パッケージ内）。
- **Out of scope**:
  - MOVIN ソース自身の通信プロトコルや uOSC 実装変更（`jp.co.unvgi.realtimeavatarcontroller.movin` の責務）。
  - `MoCapSourceFactoryRegistry` への MOVIN typeId 登録手順の変更（既存 `MovinMoCapSourceFactory.RegisterRuntime` が `RuntimeInitializeOnLoadMethod` で実施済みのため、本パッケージは登録に関与しない）。
  - 既定 `StubMoCapSourceConfigFactory` の置き換えや削除。
  - PlayMode / 実行時のアバター・ボーン解決ロジック（`MovinMotionApplier` 側責務）。
  - 複数 MOVIN ソースの同時起動・ポート競合解決の高度な制御（本仕様は Provider の設定値をそのまま渡すのみ）。
- **Adjacent expectations**:
  - `rac-main-output-adapter` パッケージ：本仕様で `RacMainOutputAdapterHost` に DI seam を追加し、`IMoCapSourceConfigFactoryProvider` 契約をクロスパッケージで参照可能な位置（具体配置は設計フェーズで確定）に提供する。
  - `realtimeavatarcontroller.movin` パッケージ：`MovinMoCapSourceConfig` / `MovinMoCapSourceFactory.MovinSourceTypeId` を変更なしで再利用する。
  - `integrated-demo` パッケージ：`IntegratedDemoBootstrap` の `EnsureMainOutputAdapters()` を拡張して MOVIN Provider を自動配線する。
  - 既定の `StubMoCapSourceConfigFactory` フォールバック挙動は維持される。

## Requirements

### Requirement 1: 新規ブリッジパッケージの提供
**Objective:** As a Unity プロジェクト保守者, I want 既存の MOVIN ランタイムと `rac-main-output-adapter` を繋ぐ独立したローカルパッケージを得る, so that 依存関係と責務分離を保ったまま MOVIN MoCap を Slot ライフサイクルに統合できる。

#### Acceptance Criteria
1. The rac-movin-mocap-factory Package shall be placed at `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/` と一致するディレクトリに配置される。
2. The rac-movin-mocap-factory Package shall declare `name = "jp.co.unvgi.vtuber-system-base.rac-movin-mocap-factory"` を `package.json` に持つ。
3. The rac-movin-mocap-factory Package shall declare `jp.co.unvgi.vtuber-system-base.rac-main-output-adapter` と `jp.co.unvgi.realtimeavatarcontroller.movin` を `package.json` の `dependencies` に列挙する。
4. The rac-movin-mocap-factory Package shall provide Runtime / Editor / Tests それぞれの asmdef を必要に応じて分離し、Runtime asmdef が `RealtimeAvatarController.MoCap.Movin` および `VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints` の型を解決できる参照を持つ。
5. If 既存の `StubMoCapSourceConfigFactory` フォールバック経路が呼び出された場合, then the rac-main-output-adapter Bootstrapper shall 既存と同じ Stub 動作を維持する（本パッケージはフォールバックを破壊しない）。

### Requirement 2: MovinMoCapSourceConfigFactory の契約
**Objective:** As a Slot ライフサイクル消費側コード, I want `IMoCapSourceConfigFactory.Build(slotId)` 経由で MOVIN 用の `MoCapSourceDescriptor` を受け取りたい, so that Slot 単位で MOVIN MoCap ソースを起動できる。

#### Acceptance Criteria
1. The MovinMoCapSourceConfigFactory shall implement `VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints.IMoCapSourceConfigFactory`.
2. When `Build(slotId)` is called, the MovinMoCapSourceConfigFactory shall return a `MoCapSourceDescriptor` whose `SourceTypeId` equals `MovinMoCapSourceFactory.MovinSourceTypeId` (`"MOVIN"`).
3. When `Build(slotId)` is called, the MovinMoCapSourceConfigFactory shall set `Descriptor.Config` to a freshly created `ScriptableObject.CreateInstance<MovinMoCapSourceConfig>()` インスタンス.
4. Where overrides for `port` are configured on the Factory, the MovinMoCapSourceConfigFactory shall apply that `port` value (1〜65535) to the produced `MovinMoCapSourceConfig` インスタンス.
5. Where overrides for `rootBoneName` are configured on the Factory, the MovinMoCapSourceConfigFactory shall apply that `rootBoneName` 文字列を produced `MovinMoCapSourceConfig` インスタンスに設定する.
6. Where overrides for `boneClass` are configured on the Factory, the MovinMoCapSourceConfigFactory shall apply that `boneClass` 文字列を produced `MovinMoCapSourceConfig` インスタンスに設定する.
7. When `Build(slotId)` is called で同一 Factory に対して複数回呼び出された場合, the MovinMoCapSourceConfigFactory shall 各呼び出しごとに独立した `MovinMoCapSourceConfig` インスタンスを返す（共有しない）.
8. The MovinMoCapSourceConfigFactory shall set the produced `MovinMoCapSourceConfig.name` を `slotId` を含む識別可能な文字列に設定する（Stub 実装と同じ命名方針に揃える）.

### Requirement 3: MovinMoCapSourceConfigFactoryProvider MonoBehaviour
**Objective:** As a Unity 利用者, I want Inspector から MOVIN Factory の port / rootBoneName / boneClass を編集できる Provider MonoBehaviour を得る, so that コード変更なしで MOVIN MoCap の設定を切り替えられる。

#### Acceptance Criteria
1. The MovinMoCapSourceConfigFactoryProvider shall be a `MonoBehaviour` であり、`IMoCapSourceConfigFactoryProvider` を実装する.
2. The MovinMoCapSourceConfigFactoryProvider shall expose `[SerializeField] private int port` と等価な serialized field を Inspector 上に提示し、既定値を `11235` とする.
3. The MovinMoCapSourceConfigFactoryProvider shall expose `[SerializeField] private string rootBoneName` と等価な serialized field を Inspector 上に提示する.
4. The MovinMoCapSourceConfigFactoryProvider shall expose `[SerializeField] private string boneClass` と等価な serialized field を Inspector 上に提示する.
5. When `Factory` プロパティが取得される場合, the MovinMoCapSourceConfigFactoryProvider shall その時点の serialized 値（port / rootBoneName / boneClass）が反映された `MovinMoCapSourceConfigFactory` インスタンスを返す.
6. When `Factory.Build(slotId)` が呼び出される場合, the MovinMoCapSourceConfigFactoryProvider shall その Factory が Provider に設定された port / rootBoneName / boneClass を `MovinMoCapSourceConfig` に伝搬することを保証する.
7. The MovinMoCapSourceConfigFactoryProvider shall be configured を、`RacMainOutputAdapterHost` と同一 GameObject に共存可能な MonoBehaviour として設計する（`DisallowMultipleComponent` を必要に応じて付与）.

### Requirement 4: IMoCapSourceConfigFactoryProvider 契約
**Objective:** As a `RacMainOutputAdapterHost` の DI seam 設計者, I want Provider MonoBehaviour を疎結合に解決できるインタフェース契約を持つ, so that MOVIN 以外の MoCap ソース（Stub / 将来追加分）も同じ seam で差し替えられる。

#### Acceptance Criteria
1. The IMoCapSourceConfigFactoryProvider Interface shall expose `IMoCapSourceConfigFactory Factory { get; }` プロパティを単一の必須メンバーとして定義する.
2. The IMoCapSourceConfigFactoryProvider Interface shall be クロスパッケージ参照可能な位置（`rac-main-output-adapter` 内 もしくは 同等の共有点）に配置される（最終配置は設計フェーズで確定）.
3. While `Factory` プロパティが未初期化の場合, the IMoCapSourceConfigFactoryProvider 実装 shall `null` を返してよい（Host 側で null を許容しフォールバックする契約）.
4. The IMoCapSourceConfigFactoryProvider Interface shall not require MOVIN 固有の API を公開せず、汎用 `IMoCapSourceConfigFactory` のみを露出する.

### Requirement 5: RacMainOutputAdapterHost の DI seam 拡張
**Objective:** As a 利用者プロジェクト, I want `RacMainOutputAdapterHost` Inspector に MoCap Factory Provider 用 SerializeField を追加してもらう, so that Provider MonoBehaviour を Inspector ドラッグまたは reflection で差し込める。

#### Acceptance Criteria
1. The RacMainOutputAdapterHost shall expose `[SerializeField] private MonoBehaviour _mocapFactoryProviderBehaviour` と等価な serialized field を追加し、既存の `_coreIpcBusProviderBehaviour` と同じ Provider パターンに従う.
2. While `_mocapFactoryProviderBehaviour` が `null` または `IMoCapSourceConfigFactoryProvider` を実装していない場合, the RacMainOutputAdapterHost shall `OverrideServices(mocapFactory: ...)` を呼び出さず、既存の `StubMoCapSourceConfigFactory` フォールバックに任せる.
3. When `Start()` が実行されかつ `_mocapFactoryProviderBehaviour` が `IMoCapSourceConfigFactoryProvider` を実装する場合, the RacMainOutputAdapterHost shall `provider.Factory` を取得し、それが非 null なら `bootstrapper.OverrideServices(mocapFactory: provider.Factory)` を `bootstrapper.Initialize()` の前に呼び出す.
4. If `provider.Factory` が `null` を返した場合, the RacMainOutputAdapterHost shall `OverrideServices(mocapFactory: ...)` を呼ばず、警告ログを出力したうえで Stub フォールバックを許容する.
5. The RacMainOutputAdapterHost shall preserve 既存の `_coreIpcBusProviderBehaviour` / `_outputSceneBootstrapper` / `OverrideMessageSink` の挙動を変更せず、本変更が後方互換であることを保証する.
6. While `Application.isPlaying` が `false` の場合, the RacMainOutputAdapterHost shall `_mocapFactoryProviderBehaviour` を参照しない（Edit モードで副作用を発生させない）.

### Requirement 6: IntegratedDemoBootstrap による自動配線
**Objective:** As a Integrated Demo シーン利用者, I want `IntegratedDemoBootstrap` が起動時に MOVIN Provider を自動的に Host に紐付ける, so that デモ起動だけで MOVIN MoCap が Slot に接続される（手動配線不要）。

#### Acceptance Criteria
1. When `IntegratedDemoBootstrap` の `EnsureMainOutputAdapters()` が実行される場合, the IntegratedDemoBootstrap shall `RacMainOutputAdapterHost` と同一 GameObject に `MovinMoCapSourceConfigFactoryProvider` コンポーネントを `AddComponent` する（既に存在する場合は再利用）.
2. After `MovinMoCapSourceConfigFactoryProvider` が GameObject に存在する状態が成立した時点で, the IntegratedDemoBootstrap shall reflection を用いて `RacMainOutputAdapterHost._mocapFactoryProviderBehaviour` フィールドにその Provider インスタンスを代入する.
3. The IntegratedDemoBootstrap shall perform 上記の AddComponent と reflection 注入を `RacMainOutputAdapterHost.Start()` が実行される前に完了させる（`DefaultExecutionOrder` の順序関係で `Initialize()` 前に Provider が見えること）.
4. If reflection による `_mocapFactoryProviderBehaviour` 代入が失敗した場合, the IntegratedDemoBootstrap shall 警告ログを出力した上で、既存の Stub フォールバック動作を阻害せずに継続する.
5. The IntegratedDemoBootstrap shall not modify 既存の `EnsureMainOutputAdapters()` 内の他の配線処理（Output Renderer Shell の生成、`_coreIpcBusProviderBehaviour` の解決など）の挙動.

### Requirement 7: 既定 Stub フォールバックの保全
**Objective:** As a 既存の Stub MoCap 動作に依存するテスト・利用者, I want 本パッケージ追加後も Stub フォールバックが等価に動作することを保証されたい, so that MOVIN を有効化しないシーン・テストが影響を受けない。

#### Acceptance Criteria
1. While `RacMainOutputAdapterHost` の `_mocapFactoryProviderBehaviour` が未設定または無効な場合, the RacMainOutputAdapterBootstrapper shall `Initialize()` 内のフォールバック `_mocapFactory ??= new StubMoCapSourceConfigFactory();` を実行し、Stub Source 挙動を維持する.
2. The rac-movin-mocap-factory Package shall not register MOVIN typeId を `RegistryLocator.MoCapSourceRegistry` に追加で登録しない（既存 `MovinMoCapSourceFactory.RegisterRuntime` の `RuntimeInitializeOnLoadMethod` 一回登録に依存する）.
3. The rac-movin-mocap-factory Package shall not modify or delete `StubMoCapSourceConfigFactory` / `StubMoCapSourceConfig` の既存型を変更しない.
4. If MOVIN typeId 登録が `RegistryConflictException` で失敗した場合（例: 二重登録）, the rac-movin-mocap-factory Package shall その例外伝播を引き起こさない（登録は MOVIN 本体の責務であり本パッケージは関与しない）.

### Requirement 8: Editor / PlayMode 副作用の境界
**Objective:** As a エディタ作業者, I want Provider や Host が Edit モードで MOVIN 接続（uOSC リスナーなど）を開始しないことを保証されたい, so that エディタ操作中に意図しない通信副作用が発生しない。

#### Acceptance Criteria
1. While `Application.isPlaying` が `false` の場合, the MovinMoCapSourceConfigFactoryProvider shall `Factory` プロパティ取得時に MOVIN ソース実体（`MovinMoCapSource` や uOSC リスナー）を生成・起動しない（純粋に Config 生成までに留まる）.
2. The MovinMoCapSourceConfigFactory shall in `Build(slotId)`, only create a `MovinMoCapSourceConfig` ScriptableObject であり、`MovinMoCapSource` のインスタンス化や uOSC ポートの bind は行わない（Source インスタンス化は RAC 側 Slot ライフサイクルの責務である）.
3. The RacMainOutputAdapterHost shall not access `_mocapFactoryProviderBehaviour` in `Awake()` or before `Start()` で `Application.isPlaying == true` が確認される前に Provider を呼び出さない.
4. While Slot が Active 状態に遷移するまでの間, the MovinMoCapSourceConfigFactory shall not produce side effects beyond `ScriptableObject.CreateInstance` および serialized 値の代入（uOSC 接続は Slot Active 時に RAC 側で開始される）.

### Requirement 9: EditMode テストによる検証
**Objective:** As a 本仕様の品質保証担当, I want 新規パッケージに EditMode テストを同梱する, so that リファクタや依存更新時に契約退行を即座に検出できる。

#### Acceptance Criteria
1. The rac-movin-mocap-factory Tests shall include an EditMode test that asserts `new MovinMoCapSourceConfigFactory().Build("slot-A").SourceTypeId == "MOVIN"`.
2. The rac-movin-mocap-factory Tests shall include an EditMode test that asserts `Build("slot-A").Config is MovinMoCapSourceConfig`.
3. The rac-movin-mocap-factory Tests shall include an EditMode test that asserts a Provider GameObject に設定された serialized `port` / `rootBoneName` / `boneClass` 値が、`Provider.Factory.Build("slot-A").Config` 上の `MovinMoCapSourceConfig` に伝搬していること.
4. The rac-movin-mocap-factory Tests shall include an EditMode test that asserts Provider の `port` 既定値が `11235` であること.
5. The rac-movin-mocap-factory Tests shall include an EditMode test that asserts `Build` を 2 回呼び出した際に異なる `MovinMoCapSourceConfig` インスタンス（参照非同一）が返されること.
6. Where 統合検証が可能な場合, the rac-movin-mocap-factory Tests shall include an optional EditMode integration test that asserts `RacMainOutputAdapterBootstrapper` に MOVIN Factory を `OverrideServices` で差し込んだとき、`Initialize()` 後にフォールバックの `StubMoCapSourceConfigFactory` ではなく差し込んだ MOVIN Factory が利用されることを観測可能な経路で確認する.
7. If テストが Unity の Test Runner 上で実行された場合, the rac-movin-mocap-factory Tests shall すべての必須テスト（上記 1〜5）が成功する状態でリリースされる.
