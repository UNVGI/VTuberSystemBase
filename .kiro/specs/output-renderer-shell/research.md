# Research & Design Decisions — output-renderer-shell

---
**Purpose**: 本 spec の設計判断に必要な調査結果・検討トレード・採用根拠を記録する。`design.md` 本体から切り出す詳細調査ログのアーカイブ。
---

## Summary

- **Feature**: `output-renderer-shell`
- **Discovery Scope**: New Feature（Wave 2 基盤）
- **Key Findings**:
  1. `UnityEngine.Display.displays[n].Activate()` は StandalonePlayer でのみ有効であり、一度アクティブ化したディスプレイは非アクティブ化できない。Editor では単一ゲームビューに限定され、Editor PlayMode では常に Display 1 相当にしか描画されないため、Editor 固有挙動の明文化が必要（OR-1 の Display 1 フォールバックと整合）。
  2. `core-ipc-foundation` の WebSocket サーバロール（D-4）と Unity メインスレッド配信契約（D-3）が前提。メインスレッド配信の実装は `UnitySynchronizationContext` を経由する既存の `core-ipc-foundation` 機構に委譲し、本 spec 側では **PlayerLoop の独自挿入は行わない**（責任境界を二重化しない）。
  3. URP の Global Volume はシーン内の GameObject として配置し、`VolumeComponent` の Override は **スタブのみ**を本 spec で提供する（具体 Override は Wave 3 のステージタブ／カメラタブの責務）。URP Default Volume Profile とは別に、Priority 0 の空の Global Volume を明示配置することで、後続タブの Override 差し込み点を安定化できる。

## Research Log

### Topic: Display.displays[n].Activate() の制約

- **Context**: Requirement 2（Display 2+ への全画面表示切替）の暫定実装を規定するため、`Display` API の制約を正確に把握する必要があった。
- **Sources Consulted**:
  - [Unity Manual - Multi-display (6000.x)](https://docs.unity3d.com/Manual/MultiDisplay.html)
  - [Unity Discussions - Multiple Displays](https://discussions.unity.com/t/multiple-displays/659705)
  - [Unity Discussions - Multiple Displays in Editor simultaneously](https://discussions.unity.com/t/multiple-displays-in-editor-simultaneously/691327)
- **Findings**:
  - Display 0 は常にアクティブで、追加ディスプレイは `Display.displays[i].Activate()` を明示的に呼ぶ必要がある。
  - `Display.Activate()` は **一度呼び出すと非アクティブ化できない**（PlayMode 内では）。
  - Editor では Game View が単一であるため、ビルド時の Display 2+ の挙動は Editor で完全再現できない。Editor では Display 1 相当にしか描画されない既知の挙動。
  - Windows 限定で `Display.Activate(width, height, refreshRate)` オーバーロードが存在する。
  - `Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen` / `FullScreenWindow` 指定と併用することで、全画面表示とディスプレイ割当を組み合わせる。
  - Camera 側は `Camera.targetDisplay` プロパティで出力先ディスプレイインデックスを指定する（`UniversalAdditionalCameraData` 側ではなく `Camera` 基底クラスの設定）。
- **Implications**:
  - 暫定実装 `BuiltInDisplayRoutingService`（本 spec で定義）は、StandalonePlayer 経路と Editor 経路で振る舞いを分岐させる必要がある。Editor では「Display 2 以降を論理的に要求するが、実際の描画は Game View = Display 1 相当で行われる」挙動を明文化する（Requirement 6.8 の Editor 固有挙動差の範囲内）。
  - 一度 Activate した後はアクティブ化状態を撤回できないため、PlayMode 停止→再開を跨ぐ状態保持を避ける（D-9 の PlayMode 完全シャットダウン契約と整合）。Editor 再起動を跨いだディスプレイ割当変更は推奨されない運用ガイダンスとして診断 API に表示する。
  - Display 2 不在時のフォールバック（OR-1）は、「`Display.displays.Length < 2` を検出→ Camera.targetDisplay=0 のまま描画継続→警告ログ」のフローで実現する。

### Topic: Unity メインスレッド配信とディスパッチ機構

- **Context**: Requirement 3（ディスパッチャ）の AC 4 が「Unity メインスレッド上でハンドラを呼び出す」ことを要求する。`core-ipc-foundation` D-3 の継承契約との責任境界を明確にする必要があった。
- **Sources Consulted**:
  - [Unity Scripting API - PlayerLoop](https://docs.unity3d.com/ScriptReference/LowLevel.PlayerLoop.html)
  - [Unity Discussions - Understanding SynchronizationContext and UnitySynchronizationContext](https://discussions.unity.com/t/understanding-synchronizationcontext-and-unitysynchronizationcontext/1700147)
  - [Unity Technologies - UnitySynchronizationContext.cs (GitHub)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs)
- **Findings**:
  - Unity 6 では `UnitySynchronizationContext` が `SynchronizationContext.Current` にバインドされており、`async/await` の継続は自動的にメインスレッドで再開する。
  - PlayerLoop に独自 `PlayerLoopSystem` を挿入する方法は可能だが、複雑でありフレームワーク層で 1 本化することが推奨される。
  - メインスレッド配信の責任を重複させると、タイミング差（Update/LateUpdate/FixedUpdate のどこで呼ばれるか）が spec ごとに変わり得てバグの温床となる。
- **Implications**:
  - 本 spec のディスパッチャは **`core-ipc-foundation` が公開する「メインスレッド配信済みコールバック」を前提とした同期 API 呼び出し**として実装する。つまり、本 spec 側では「スレッド切替」を行わず、「`core-ipc-foundation` から呼ばれた時点で既にメインスレッド上である」前提でハンドラルックアップと invoke を行う。
  - これにより、`core-ipc-foundation` 側の R-4（PlayerLoop ディスパッチの具体タイミング）を本 spec で再定義しない。`core-ipc-foundation` が Update / LateUpdate / FixedUpdate いずれを選んでも、本 spec の契約は不変。

### Topic: URP Global Volume の設計配置

- **Context**: Requirement 1.5（空の Global Volume を拡張点として配置）の実装方針を確定するため、URP の Volume システムの運用前提を把握する必要があった。
- **Sources Consulted**:
  - [Unity Manual - Set up a volume in URP (6000.0)](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/set-up-a-volume.html)
  - [Unity Manual - Understand volumes in URP (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/Volumes.html)
  - [Unity Manual - Troubleshooting volumes (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/volumes-troubleshooting.html)
  - [Unity Discussions - URP/Volume.cs access override settings at runtime](https://discussions.unity.com/t/urp-volume-cs-how-to-access-the-override-settings-at-runtime-via-script/773268)
- **Findings**:
  - URP の Volume は `Volume` コンポーネントを持つ GameObject + `VolumeProfile` アセットの組合せで構成される。`isGlobal = true` にすると Global Volume となりシーン全体に影響する。
  - URP Default Volume Profile（Project Settings > Graphics > URP > Default Volume Profile）はシーンロード／品質変更時のみ評価されるキャッシュベース。ランタイムで頻繁に Override を書き換えるユースケースには Scene 内 Volume を使うほうが確実。
  - `VolumeComponent` は `VolumeParameter<T>` 型のフィールドで Override 値を保持し、`VolumeManager.stack.GetComponent<T>()` で取得可能。
  - Priority は上書き優先度。デフォルトは 0。
- **Implications**:
  - 本 spec では Scene 配下の GameObject として Global Volume を明示配置する（Priority 0、空の VolumeProfile）。これにより Wave 3 のタブ spec は VolumeProfile に Override を差し込むだけで反映できる。Default Volume Profile には手を入れない。
  - 「空の Global Volume」とは **Volume コンポーネント + 空の VolumeProfile（Override 0 件）** を指す。VolumeProfile 自体は実体として存在させ、後続 spec が AddComponent<T>() で Override を増やす前提。
  - 本 spec の Service Locator（`IOutputSceneRoots`）に Global Volume の `VolumeProfile` 参照取得 API を含める必要がある。

### Topic: Unity Crash Handler / Development Build Overlay の配信適合性

- **Context**: Requirement 5.4（Unity 既定のエラーダイアログ・クラッシュダイアログ・Development Build のオーバーレイが Display 2+ に出ないことを保証する）の実装方針。
- **Sources Consulted**:
  - [Unity Discussions - What is the Unity Crash Handler?](https://discussions.unity.com/t/what-is-the-unity-crash-handler-do-i-need-it/799609)
  - [Unity Discussions - Disable crash dialog](https://discussions.unity.com/t/disable-crash-dialog/480774)
  - [Unity ScriptReference - CrashReportingSettings](https://docs.unity3d.com/ScriptReference/CrashReporting.CrashReportingSettings.html)
  - [Unity Discussions - Standalone disable crash message](https://discussions.unity.com/t/standalone-disable-crash-message/175787)
- **Findings**:
  - UnityCrashHandler.exe は別プロセスのダイアログとしてプロセス外に表示されるため、Display 2 に「描画」されることは原理的にない。ただしウィンドウは OS のウィンドウマネージャが配置するため、物理的にはどのモニタに出るかは OS 依存。
  - Development Build のオーバーレイ（Debug.LogError の画面表示等）は Display 0 / Display 1 を優先して表示される。
  - `Application.logMessageReceived` でログイベントを捕捉して Debug.LogError を Main Output ディスプレイ上に描画する既定 UI を無効化する方法は Unity 標準では提供されていないが、本 spec ではそもそも `OnGUI` / `IMGUI` を Main Output カメラ側にアタッチしない構成を取るため影響しない。
  - Development Build の "Stats" や "Profiler" オーバーレイは Game View にしか出ず Build には出ない。
- **Implications**:
  - Requirement 5.4 は「本 spec が自発的に描画する GUI を一切アタッチしない」契約と「Development Build オーバーレイは Display 1 側 Game View に出ることを運用規約として明記」の 2 点で充足できる。
  - UnityCrashHandler.exe のウィンドウ位置は OS に依存するため、運用ガイダンス（「クラッシュ時に物理的に Display 2 上にダイアログが出る可能性はゼロではない」）を診断ドキュメントに明記する。プロセスが生きている限り描画は継続するため、クラッシュ時点では既に配信事故回避の対象外（プロセス停止）として扱う。
  - 本 spec が能動的にできる対策は「`Application.quitting` / `logMessageReceived` を捕捉して UI 側へ転送する」経路のみを提供することに絞る。

### Topic: Camera のレイヤー／カリングマスク設計

- **Context**: Requirement 5.1（メイン出力カメラのカリングマスクにオペレーター UI レイヤーを含めない）の実装方針。
- **Sources Consulted**: Unity Manual Camera, Layer 標準知識。
- **Findings**:
  - Unity は 32 レイヤー（0..31）を持ち、そのうち `Default`, `TransparentFX`, `IgnoreRaycast`, `Water`, `UI` 等がビルトイン予約。
  - Camera.cullingMask は bitmask で、`~(1 << LayerMask.NameToLayer("UI"))` のように除外可能。
  - UI Toolkit の PanelSettings.targetDisplay で論理ディスプレイを指定できるため、レイヤーではなく targetDisplay レベルで UI を分離する設計も併用可能。
- **Implications**:
  - 本 spec のメイン出力カメラのカリングマスクは明示的に「ステージ・キャラクター・Light ルート配下に使うレイヤーのみ」を含める設計にする。ただし具体レイヤー名は Wave 3 の各タブが追加する GameObject に依存するため、本 spec は **「UI 専用レイヤーを除外する」という契約**（Everything & ~UI レイヤー群）のみを確定させる。具体レイヤー定義は後続 spec の責務。
  - UI Toolkit の targetDisplay = 0（Display 1）に固定されることは `ui-toolkit-shell` Requirement 1.2 で保証される。本 spec のメイン出力カメラ `targetDisplay >= 1` と併せて、表示分離は二重に担保される。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A. Service Locator + Dispatcher | シーン初期化で各ルート GameObject と Global Volume を生成し、名前解決で後続 spec に提供。ディスパッチャはハンドラ登録テーブル | 依存方向がシンプル、テスト容易、疎結合 | サービスロケータのアンチパターン懸念（暗黙依存）。ただし本 spec の責務が「シーン骨格の提供」に限定されるため許容 | 採用 |
| B. DI コンテナ（Zenject 等）| 依存注入で全てを配線 | 大規模で拡張性高 | 本フェーズには過剰。外部 DI 依存の追加は YAGNI | 却下 |
| C. MonoBehaviour 直接アクセス（`FindObjectOfType`）| 各タブが `FindObjectOfType<GlobalVolumeRoot>()` で直接取得 | 実装最小 | 検索コストとテスト困難性、契約が不明瞭 | 却下 |

**Selected**: A（Service Locator + Dispatcher）。本 spec のスコープと Requirement 1.7（安定した命名規約または参照取得 API）に直接対応する。

## Design Decisions

### Decision: ディスプレイ切替サービスの差し替え境界

- **Context**: Requirement 2.1, 2.5, 2.6 で暫定実装と将来の RDS 差し替えを両立させる必要がある。ディスパッチャ・シーン初期化コードを変更せずに実装を差し替えるための接合点を決める必要があった。
- **Alternatives Considered**:
  1. **抽象インタフェース `IDisplayRoutingService` + 暫定実装 `BuiltInDisplayRoutingService`**：本 spec が interface を所有し、RDS 差し替えは DI 経路で行う。
  2. RDS パッケージのインタフェースを直接依存対象とする：spec #7 未実装の現状では参照不能。
  3. `ScriptableObject` ベースの戦略パターン：Inspector で差し替え可能。
- **Selected Approach**: 1 を採用。本 spec 内に `IDisplayRoutingService` interface と `BuiltInDisplayRoutingService` を定義し、Composition Root で具体実装を差し込む（Unity 側では `MonoBehaviour` Bootstrapper が `IDisplayRoutingService` を生成し、`OutputSceneBootstrapper` に渡す）。
- **Rationale**:
  - 暫定実装を interface の実装クラスとして隔離することで、ディスパッチャ・シーン初期化コードが具体型に依存しない（Requirement 2.5 の直接反映）。
  - RDS パッケージの仕様が未確定であっても、本 spec は「インタフェース契約」のみ安定化させれば良く、spec #7 は同インタフェースの別実装を提供するだけで済む。
- **Trade-offs**:
  - 利点: 疎結合、テスト時のモック差し替え可能（Requirement 8.5 に直接対応）、spec #7 の影響範囲最小化。
  - 欠点: interface の設計が不完全だと RDS 実装時に破壊的変更が必要になるリスク。対応策として interface を最小限（`Activate`, `GetAssignedDisplayIndex`, `IsFallbackActive` 等）に絞る。
- **Follow-up**:
  - 実装時に RDS リポジトリ（https://github.com/Hidano-Dev/RuntimeDisplaySelector）の公開 API を参照し、interface シグネチャを擦り合わせる。spec #7 作業前に大きな破壊的変更がないかを spec #7 kickoff 時に再確認する。

### Decision: ディスパッチャと core-ipc-foundation の責任境界

- **Context**: Requirement 3.1（サーバロール起動）と 3.4（Unity メインスレッド上でハンドラ呼び出し）の責任境界。スレッドマーシャリングを本 spec で再実装するかの判断。
- **Alternatives Considered**:
  1. **本 spec のディスパッチャは「既にメインスレッド上で呼ばれる」前提で動作**：`core-ipc-foundation` が D-3 のメインスレッド配信を保証。本 spec はハンドラテーブルとルックアップ／invoke のみ担当。
  2. 本 spec で独自の SynchronizationContext 経由ディスパッチを持つ：`core-ipc-foundation` の D-3 を信頼せず二重マーシャリング。
- **Selected Approach**: 1 を採用。本 spec のディスパッチャ `IOutputCommandDispatcher` は同期 API として提供し、呼び出し側（= `core-ipc-foundation` の受信コールバック）が既にメインスレッド上であることを不変条件とする。
- **Rationale**:
  - D-3（受信コールバックは常に Unity メインスレッドで配信）を信頼する契約継承を明示化する。spec 間の契約の多重定義を避け、バグ境界を単純化。
  - ディスパッチャの invoke パスから余計な `SynchronizationContext.Post` を省くことで、最小レイテンシでハンドラが起動する。
- **Trade-offs**:
  - 利点: 実装シンプル、テスト容易、spec 間の責任境界が明確。
  - 欠点: `core-ipc-foundation` の D-3 契約違反があった場合に本 spec 側で Unity API 呼び出しが壊れるが、これは契約違反として扱う（invoke 時点でスレッド ID をアサートし、違反時は診断ログ + 例外捕捉で描画継続）。
- **Follow-up**: テスト時に `IOutputCommandDispatcher.Dispatch()` にワーカースレッドから呼び出すネガティブケースを加え、診断ログが出ることを検証する。

### Decision: サーバロール起動のライフサイクル設計

- **Context**: Requirement 3.1 / Requirement 6（PlayMode / スタンドアロン両対応）で、WebSocket サーバ起動のタイミングをどのように統一するか。
- **Alternatives Considered**:
  1. **Composition Root のブートストラッパー `OutputSceneBootstrapper`（MonoBehaviour）が Awake → Start の順序で IPC サーバ→シーン初期化→ディスプレイ切替→ディスパッチャ登録受付開始を逐次起動**。
  2. `[RuntimeInitializeOnLoadMethod]` で静的に初期化。
- **Selected Approach**: 1 を採用。シーン配下のブートストラッパー `MonoBehaviour` で明示的に順序制御する。
- **Rationale**:
  - シーンロード完了後に起動するため、シーン内の GameObject 参照が安定する。
  - PlayMode 停止で `OnDestroy` が呼ばれ、Dispose ライフサイクルをシンプルに結合できる。
  - `[RuntimeInitializeOnLoadMethod]` はシーンロード前に起動するため、シーン骨格生成の前提が崩れる。
- **Trade-offs**:
  - 利点: ライフサイクル統一、D-9（PlayMode 停止時シャットダウン）に自然対応。
  - 欠点: シーンに必ず 1 つ `OutputSceneBootstrapper` GameObject が必要。これは「シーンの契約」として明示する。
- **Follow-up**: `OutputSceneBootstrapper` は単一インスタンス制約を持つ。重複配置時は `Awake` で警告ログ + 2 つ目以降を自己破棄する安全策を入れる。

### Decision: OR-2 の Last-write-wins をどこで実装するか

- **Context**: Requirement 4.8（複数クライアントの state 競合は last-write-wins）の実装箇所。
- **Alternatives Considered**:
  1. **`core-ipc-foundation` が受信キュー段階で同一トピックを最新値に coalesce し、本 spec は受信した単一値を適用するだけ**：D-7 / D-10 と整合し、クライアント単位の区別は不要。
  2. 本 spec のディスパッチャでクライアント ID 単位に保持し、最後に到着したクライアントの値を採用。
- **Selected Approach**: 1 を採用。`core-ipc-foundation` Requirement 9.1 の「受信側で `kind = state` は同一トピックで coalesce」は **クライアント区別なしの最新値優先**として読むことで、OR-2 の last-write-wins は上流基盤の既存契約で自然に実現される。本 spec 側には追加実装を持たない。
- **Rationale**:
  - クライアント識別情報は `core-ipc-foundation` のエンベロープには必須ではなく、本 spec でクライアント単位 ID を保持しても YAGNI。
  - OR-2 の「Last-write-wins = 最後に到着した state コマンドを常に採用」は「同一トピック最新値優先」と等価。
- **Trade-offs**:
  - 利点: 実装追加ゼロ、契約単純。
  - 欠点: 将来のクライアント単位排他制御（OR-2 の R-2）が必要になった場合、`core-ipc-foundation` のエンベロープ拡張と本 spec のディスパッチャ両方に手が入る可能性。本フェーズでは YAGNI として許容。
- **Follow-up**: Requirement 4.8 の実装は「`core-ipc-foundation` の既存契約を引き継ぐ」旨のテストケース（複数 mock client が同トピック state を連続送信して最後の値が勝つ）を Requirement 8 テスト群に含める。

## Risks & Mitigations

- **R-OR-1: Display 2 不在時のフォールバック運用リスク（OR-1 継承）**
  - Display 2 が物理的に切断された状態で配信開始すると Display 1 に UI とメイン出力が重なる（Display 1 上に UI Toolkit Shell の UIDocument と Camera 描画が同居）。設計フェーズで「UI 側に目立つ誤配信警告を出す診断 API 連携」を Requirement 2.4a で既に受け皿を用意済み。実装時に UI 側との UX 契約（R-1 残留リスクとして記載）を確立する。
  - **Mitigation**: 診断 API `IOutputDiagnostics.GetDisplayAssignment()` を公開し、UI 側が起動直後と接続確立直後に必ず取得して `isFallbackActive == true` を検出したら赤色警告バッジを表示する運用契約を確立する（spec #3 ui-toolkit-shell Requirement 9.6 と整合済み）。
- **R-OR-2: Editor PlayMode での Display 2 挙動差**
  - Editor では Game View が単一で、Display 2 相当の出力は Game View 上では不可視。実装時は Editor 固有挙動を明文化し、「Editor PlayMode ではメイン出力カメラは常に Game View に描画される」ことを受け入れる。Requirement 6.8 が既にこれを許容している。
  - **Mitigation**: `BuiltInDisplayRoutingService.Activate()` 時に `Application.isEditor` 分岐で警告ログを出し、「Editor では Display 2 は非描画。スタンドアロンで検証すること」と明示。
- **R-OR-3: Global Volume の VolumeProfile 共有リスク**
  - 本 spec が空の VolumeProfile を Asset として同梱するか、Scene ローカルに生成するかで後続タブの書き換え挙動が変わる（Asset を書き換えると Git diff が出る）。Scene ローカル生成なら PlayMode 停止で消えて再現性が担保される。
  - **Mitigation**: VolumeProfile は **ランタイム生成の ScriptableObject インスタンス**（`ScriptableObject.CreateInstance<VolumeProfile>()`）として Scene ロード時に毎回生成する。これにより Asset に副作用を残さず、PlayMode 停止で自然にクリーンアップされる（D-9 と整合）。
- **R-OR-4: ハンドラ登録時の重複・競合**
  - 複数タブが同じ `topic` を登録したらどうするか（Requirement 3.3 未明示）。
  - **Mitigation**: 設計上「1 topic = 1 ハンドラ」の契約を採用し、重複登録は例外 or 既存ハンドラ差し替えの 2 択。実装時にテストで確認する。本 spec では「例外 + 診断ログ」を基本方針とする（Fail-Fast）。
- **R-OR-5: 診断 API のスレッドアクセス**
  - 診断 API（`GetDisplayAssignment` 等）を UI 側が任意スレッドから呼び出す可能性がある。
  - **Mitigation**: 診断 API は読み取り専用・スレッドセーフ（volatile / lock 最小）とし、Unity API アクセスを伴う場合のみメインスレッド制約を明記する。

## References

- [Unity Manual - Multi-display (6000.x)](https://docs.unity3d.com/Manual/MultiDisplay.html) — Display.Activate の API 仕様とプラットフォーム制約。
- [Unity Manual - Set up a volume in URP (6000.0)](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/set-up-a-volume.html) — URP Global Volume の設置手順。
- [Unity Manual - Understand volumes in URP (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/Volumes.html) — Volume のランタイム評価モデル。
- [Unity Scripting API - PlayerLoop](https://docs.unity3d.com/ScriptReference/LowLevel.PlayerLoop.html) — 独自 PlayerLoopSystem 挿入の API（本 spec では使用せず、core-ipc-foundation に委譲）。
- [Unity Discussions - UnitySynchronizationContext](https://discussions.unity.com/t/understanding-synchronizationcontext-and-unitysynchronizationcontext/1700147) — メインスレッド配信の基盤。
- [Unity Discussions - Disable crash dialog](https://discussions.unity.com/t/disable-crash-dialog/480774) — UnityCrashHandler.exe の運用考察。
- [Unity Technologies - UnitySynchronizationContext.cs (GitHub)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs) — Unity 公式のメインスレッド SyncContext 実装。
- [UnityMainThreadDispatcher (GitHub, reference pattern)](https://github.com/gustavopsantos/UnityMainThreadDispatcher) — ディスパッチャパターンの一般例。
- `.kiro/specs/core-ipc-foundation/requirements.md` — 上流 spec、D-1〜D-11 の継承元。
- `docs/requirements.md` §2.3, §3.1, §3.3, §6.2 — 本 spec の上位要件。
