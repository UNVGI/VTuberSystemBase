# Research & Design Decisions — ui-toolkit-shell

---
**Purpose**: ui-toolkit-shell（UI Toolkit シェル）設計フェーズでの調査結果、アーキテクチャ検討、残留リスクへの仮置き回答を記録する。

**Usage**:
- requirements.md の UI-1〜UI-7 および R-1〜R-8 を基点に、設計判断と根拠を文書化する。
- design.md に載せ切れない比較検討・API 調査・代替案却下理由を保管する。
---

## Summary

- **Feature**: `ui-toolkit-shell`
- **Discovery Scope**: New Feature（Wave 1 新規シェル）
- **Key Findings**:
  - UI Toolkit の PanelSettings は `targetDisplay` プロパティで描画先ディスプレイを指定でき、複数の UIDocument が同一 PanelSettings を共有する構成が標準パターン。3 タブそれぞれを独立した UIDocument として同一 PanelSettings にアタッチし、ルートの `style.display` を切り替える方式が UI-1 / UI-2 を自然に満たす。
  - Addressables の `AsyncOperationHandle.Completed` コールバックは Unity メインスレッドで発火するため、シェル側の Completion 通知契約（Requirement 4.3）を上乗せする薄い抽象を被せるだけで D-3 を満たせる。
  - UI Toolkit 6000.3 系の `TabView` 標準コントロールは UX 実装の候補だが、「3 タブ分を別 UIDocument として事前生成して display 切替で隠す」契約（UI-1 / UI-2）を素直に表現するには、タブバー UI のみを独自実装し、タブコンテンツは 3 枚の UIDocument を並置する構成が適する。TabView を採用すると 3 タブのコンテンツを 1 つの VisualTree に集約する設計となり、タブ spec（#4〜#6）の asmdef 分離（UI-4, タブ spec の Requirement 1.7）と相性が悪いため不採用。
  - ルート UIDocument（タブバー）と 3 枚のタブ UIDocument の計 4 枚で PanelSettings を共有し、ルートが `sortingOrder` 低、タブ UIDocument はルートの直下にマウントされる参照関係とする。
  - 接続未接続時の Command 送信は「保留キュー」方式ではなく「即時エラー返却＋診断表示」方式に固める（R-5）。保留キューは state の coalesce 特性と結合させると順序崩れ・古い値の遅延反映が発生し、configuration の一貫性を壊す。

## Research Log

### Topic: UI Toolkit の PanelSettings と targetDisplay / 複数 UIDocument 構成

- **Context**: Requirement 1（Display 1 割り当て）と Requirement 3（3 タブ UIDocument の起動時プリロード・常駐）の両立可能性を確認する必要があった。
- **Sources Consulted**:
  - [Panel Settings properties reference (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Runtime-Panel-Settings.html)
  - [PanelSettings Scripting API (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.PanelSettings.html)
  - [Configure runtime UI (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-render-runtime-ui.html)
  - [Panels (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-panels.html)
  - [UI Document component (Unity 6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/UIE-create-ui-document-component.html)
  - [How do you use multiple UI documents in the same scene? (Unity Discussions)](https://discussions.unity.com/t/how-do-you-use-multiple-ui-documents-in-the-same-scene/885620)
  - [Performance of several UI documents (Unity Forum)](https://forum.unity.com/threads/performance-of-several-ui-documents.1228149/)
- **Findings**:
  - `PanelSettings.targetDisplay` で描画先の物理ディスプレイ（0-based）を指定する。`0` が Display 1 に相当する。
  - 同一 Scene に複数の `UIDocument` を置き、全てが同じ PanelSettings を参照すると、ランタイム上では単一パネル上にマウントされ、VisualTree が 1 本の木として統合される（各 UIDocument の `rootVisualElement` がパネルのルートに子として append される順序は `UIDocument.sortingOrder` に従う）。
  - UIDocument を Hierarchy に配置するだけで `OnEnable` 時に UXML のパース・clone が 1 回行われる。以降は `rootVisualElement.style.display = DisplayStyle.None` にするだけで表示を切れ、パース・clone は再実行されない。
  - Unity Discussions で観測されている複数 UIDocument 構成の性能影響は、PanelSettings を別にすると描画パスが分離されるため増えるが、同一 PanelSettings を共有すれば 1 本の合成木として扱われ、非アクティブタブ（display: none）は割り当てコストが極小に抑えられる。
- **Implications**:
  - 設計は「1 枚の PanelSettings（targetDisplay=0）+ ルート UIDocument（タブバー）+ 3 枚のタブ UIDocument」の構成で UI-1 / UI-2 を両方満たす。
  - Requirement 1.7（Display 2+ に一切描画しない）は PanelSettings 1 本に集約した `targetDisplay=0` で構造的に保証される。
  - プリロード完了判定は「全 UIDocument の `rootVisualElement != null`（OnEnable 完了）」で検出可能。シェル側で OnEnable フックを集約する `TabPanelRegistry` を設ける。

### Topic: Addressables の Completion コールバックとメインスレッド保証

- **Context**: Requirement 4.3（Completion コールバックを Unity メインスレッドで呼ぶ）の実装方式と、Requirement 4.7（重複ロード抑止 / 複数 Completion 配信）の具体化。
- **Sources Consulted**:
  - [Asynchronous operation handles (Addressables 2.0+)](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/AddressableAssetsAsyncOperationHandle.html)
  - [Synchronous loading (Addressables 2.7)](https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/SynchronousAddressables.html)
  - [LoadAssetsAsync callback vs Task completion (Unity Forum)](https://forum.unity.com/threads/in-loadassetsasync-foo-is-there-a-reason-to-prefer-the-callback-function-over-the-task-completion.702440/)
  - [LoadAssetsAsync blocks main thread (Unity Discussions)](https://discussions.unity.com/t/loadassetsasync-blocks-main-thread/913845)
- **Findings**:
  - `AsyncOperationHandle<T>.Completed` はメインスレッドで発火する。I/O 自体はワーカースレッドで走り、完了通知だけが PlayerLoop に戻される。
  - Addressables 2.x では `LoadAssetAsync` の戻り値 `AsyncOperationHandle<T>` が参照カウントを内包しており、同一アドレスへの重複要求はライブラリ側でキャッシュ／カウント管理される。シェル層は追加の重複検知を実装しなくても、`Release()` を対称に呼び出す契約だけ利用者に要求すれば十分。
  - `WaitForCompletion()` はメインスレッドをブロックするため、本 spec では **禁止** する（Requirement 4.2, 4.6 違反）。シェルの公開 API にはそもそも同期待ち API を含めない。
- **Implications**:
  - 「非同期ロード基盤」は Addressables 上に薄い Facade を設ける構成とする。具体的には `IAsyncAssetLoader` インタフェースと、Addressables 実装 `AddressablesAssetLoader`、テスト用 `FakeAsyncAssetLoader`（R-4 / Requirement 10.7）の 3 つ。
  - 重複ロード抑止は Addressables 内蔵の参照カウントに委譲し、シェルは「同一 key の未完了ハンドルが存在すれば既存ハンドルの Completed に購読を追加する」薄いキャッシュだけ持つ（Requirement 4.7）。
  - ハンドル所有権は「呼び出し側タブ」が持ち、アンロード（Requirement 4.8）はタブ側の責務。シェルは `Release(handle)` と `ReleaseAll(scopeId)` を提供し、scopeId はタブごとに発行する。

### Topic: TabView 採用 vs 独自タブバー

- **Context**: UI Toolkit 6000.x の `TabView`（Tab + TabView）は標準コントロール化されており、タブ切替を単独で完結できる。本 spec の UI-1 / UI-2 契約との適合度を比較する。
- **Sources Consulted**:
  - [Create a tabbed menu (Unity 6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/UIE-create-tabbed-menu-for-runtime.html)
- **Findings**:
  - `TabView` は 1 枚の UXML 内で `Tab` 要素を並べ、各 `Tab` のコンテンツを VisualTree の子として保持する構造。選択切替時は内部で表示／非表示が切り替えられる。
  - `TabView` 採用時はタブコンテンツが単一 UXML に集約される前提で API が設計されており、「別 asmdef の別 UXML を外部から Tab に注入する」正式なフック点は乏しい。
  - `view-data-key` による永続化挙動が標準でオンになる（UI-7 と衝突）。
- **Implications**:
  - タブコンテンツの所有が 3 タブ spec（#4〜#6、別 asmdef）に分かれる本プロジェクトの構造と合わない。
  - 代わりに、タブバー（3 つの Button 相当）は独自実装し、タブコンテンツは 3 枚の独立 UIDocument を display 切替する方式を採用する。UX は TabView 相当の見た目を USS クラスで再現する。
  - 将来タブ数・順序入替が必要になった場合は TabView へマイグレーション可能だが、本フェーズでは YAGNI。

### Topic: USS セレクタ命名規約（R-2 への仮置き）

- **Context**: UI-3（USS 差し込みを一次契約）を具体規約に落とし込む必要がある。
- **Sources Consulted**:
  - UI Toolkit USS ベストプラクティス（Unity マニュアル）
  - BEM 命名規約（外部標準、一般論）
- **Findings**:
  - UI Toolkit の USS セレクタはクラス名ベースで設計するのが標準。タグ・ID 指定は柔軟性に乏しく、スキン差し替え時の衝突リスクも高い。
  - プレフィクス付きクラス命名（例: `vsb-tab-bar__button--active`）は BEM の考え方を踏襲し、スキン側で特定クラスのみを上書きすれば差分スタイルが効く。
- **Implications**:
  - 本 spec の「安定 USS セレクタ規約」は `vsb-` プレフィクス + BEM 風の Block/Element/Modifier 記法で固定する（詳細は design.md § スキン拡張）。
  - 共通 UI コンポーネント（Requirement 7）もこの規約に従う。タブ spec が独自要素を追加する場合は「タブ ID」を含むクラス名（例: `vsb-tab-character__slot-card`）で衝突を避ける。

### Topic: UXML 差し替え拡張点の実装形式（R-3 への仮置き）

- **Context**: UI-3 の「UXML 差し替えはオプション」を具体的な解決機構に落とす。ScriptableObject / Addressables / 名前付き解決の選択肢があった。
- **Sources Consulted**:
  - Unity ScriptableObject パターン一般論
  - 採用済み UI-6（Addressables 標準化）との整合性
- **Findings**:
  - ScriptableObject ベースの「UI スキンプロファイル」アセットを 1 つ定義し、そこにタブごとの `VisualTreeAsset` 参照と `StyleSheet` 参照を差し替え可能なフィールドとして持たせる案が、Unity Editor から差し替えを設定する UX が最も自然。
  - Addressables 経由の動的解決は、起動時プリロード契約（UI-1）と相性が悪い（解決まで時間がかかる）。
  - 名前付き解決（Resources.Load 相当）は命名衝突リスク・パス結合リスクが高い。
- **Implications**:
  - `UiToolkitShellSkinProfile`（ScriptableObject）を拡張点として公開する。既定スキン（シェル同梱）はパッケージ内 ScriptableObject として提供し、利用者プロジェクトが「別の ScriptableObject」を PanelSettings / ShellBootstrapper 上のフィールドに差し替えることでスキン変更を適用できる。
  - タブ UXML の必須要素（必須クラス名）は各タブ spec が定義し、シェルは「検証規約」のみを提供する（Requirement 6.5, 6.6）。

### Topic: 接続未確立時の Command 送信挙動（R-5 への仮置き）

- **Context**: Requirement 9.4 の「接続未確立時の Command 送信」の具体挙動をエラー返却と保留キューから選ぶ。
- **Sources Consulted**:
  - `core-ipc-foundation` の Requirement 5（接続断時の API 挙動規定）
  - `output-renderer-shell` の Requirement 7（UI 未接続時のメイン出力振る舞い）
- **Findings**:
  - `core-ipc-foundation` は「上位がクラッシュしない契約」を提供するが、具体的に「エラー vs 保留」のどちらを返すかは上位が決めてよい（spec #1 Requirement 5.4）。
  - 保留キューを採用すると、state コマンドの coalesce が時間軸で崩れる（古い state が遅れて到着して最新値を上書きする逆転リスク）。
  - エラー返却方式だと、タブ spec 側は「接続状態を参照 → 送信」か「送信 → エラー時のローカル抑制」のどちらかを選択できる。
- **Implications**:
  - 本 spec は **エラー返却方式（`Result<SendOk, SendError>`）** を採用する。タブ spec は送信前にシェルが公開する `IConnectionStatus` を参照して UX を制御し、失敗時は診断ログに落とす契約とする（Requirement 5.9, Requirement 9.4）。
  - 接続状態は UI 側診断表示領域（Requirement 9.5）で可視化し、オペレーターが状況を判別できるようにする。

### Topic: メイン出力側 Display 1 フォールバック時の UI 警告 UX（R-6 への仮置き）

- **Context**: `output-renderer-shell` の OR-1 による Display 1 フォールバック時に、UI 側で誤配信リスクを警告する必要がある（Requirement 9.6）。
- **Sources Consulted**:
  - `output-renderer-shell` Requirement 2.4a（ディスプレイ割り当て状態の診断 API 公開）
- **Findings**:
  - メイン出力側は `Request/Response` でディスプレイ割り当て状態を返せる構造。UI シェルはこの診断 API をポーリングまたは購読する。
  - 警告 UX の粒度はシェル側の「診断/通知領域」にバッジとテキストを出すのが最小。タブ spec を改変しない経路とする。
- **Implications**:
  - 本 spec は `IMainOutputDisplayStatus` 抽象を導入し、シェルが起動時に IPC 経由で状態を取得し、`DisplayFallback = true` の場合は **タブバー直下の通知バー** に「メイン出力が Display 1 にフォールバック中：誤配信に注意」と表示する。
  - 状態が変化した際はメイン出力側から `PublishState` で通知されるトピック（例: `output/display/fallback`）を購読し、リアルタイムに通知バーを更新する。

### Topic: 初期アクティブタブの既定値（R-7 への仮置き）

- **Context**: requirements.md § Dig Summary の R-1 / R-7 の「起動直後にどのタブを開くか」。
- **Findings**:
  - 配信リハーサル・本番準備のワークフローは「キャラクター選択 → ステージ・ライティング → カメラスイッチャー」という左から右への順序（spec #4 → #5 → #6）が自然。
  - オペレーターが最初に実施するのはアクター参加確認（Slot ↔ アバター割当）であり、キャラクター選択タブが最も頻繁な起動直後タスク。
- **Implications**:
  - 既定アクティブタブは **Character Selection タブ** とする。設定ファイルで変更可能とする（次項参照）。

### Topic: プリロード失敗タブの再試行ポリシー（R-8 への仮置き）

- **Context**: Requirement 3.5 の「プリロード失敗時の挙動」の具体化。
- **Findings**:
  - 失敗要因は主に UXML パースエラー（開発時バグ）と、スキン差し替えで必須要素が欠落したケース（運用時）。
  - 手動リトライ UI を備えても、UXML パースエラーは再試行で復旧しないため意味が薄い。
- **Implications**:
  - 再試行 UI は実装しない。失敗タブは「非活性表示」で保持し、診断ログと通知バーで提示する。
  - 他タブは正常動作を継続し、シェル全体の起動は中断しない（Requirement 3.5 の直接適用）。

### Topic: プリロード所要時間の上限（R-1 への仮置き）

- **Context**: UI-1 の「起動時一括プリロード」の所要時間と UX のトレードオフ。
- **Findings**:
  - VisualTreeAsset + StyleSheet のパース・clone は概ね 1 タブあたり数 ms〜10ms 程度。3 タブ合計で 30〜50ms を超えない想定。
  - 共通 UI コンポーネントライブラリの初期化（USS 登録、カスタムコントロール登録）は `VisualElement.RegisterFactory` 相当が起動時 1 回。
  - 起動直後の 1 フレームに全てを詰め込む必要はなく、複数フレームに分割してもオペレーター操作を待たせる時間は「体感的に即座」のレベルで収まる。
- **Implications**:
  - 設計上は「起動後 1 秒以内にプリロード完了」を目標とする（docs/requirements.md §4.2 からの派生値）。測定値の継続監視は Requirement 11.1 の診断ログで実施。
  - 同期的に詰め込まず、`OnEnable` を通常の Unity ライフサイクルに従わせて、シェルは完了判定のみを集約する。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A. 独自タブバー + 3 UIDocument 並置（採用） | タブバー UIDocument 1 枚 + タブ UIDocument 3 枚を同一 PanelSettings に並置、ルートは display 切替 | UI-1 / UI-2 を直接表現、タブ spec の asmdef 分離と整合、スキン差し替えが UXML / USS 単位で完結 | タブバーと 3 タブのマウント順序・sortingOrder 管理が必要 | 本 spec 採用 |
| B. TabView 標準コントロール | 1 枚の UXML に `TabView` を置き、3 Tab 要素にタブコンテンツを配置 | 実装量最小、reorder 等の副次機能あり | タブ spec（別 asmdef）が Tab の内部に VisualTreeAsset を差し込む正式フックが乏しい、view-data 永続化が UI-7 と衝突 | 却下 |
| C. シングル UIDocument + 動的 clone | 起動時に 1 枚の UIDocument だけ生成し、タブ切替時に別の VisualTreeAsset を CloneTree する | UIDocument が 1 枚で済む | タブ切替時に clone が走り UI-2 違反、メイン出力に影響するリスク | 却下 |
| D. 3 PanelSettings + 3 UIDocument | 各タブごとに別 PanelSettings を持ち、描画パスを分離 | タブごとの描画独立性が高い | PanelSettings 3 本の targetDisplay 管理が煩雑、非アクティブタブも描画パスが走る可能性、GPU コスト増 | 却下 |

## Design Decisions

### Decision: ルート UIDocument 1 枚 + タブ UIDocument 3 枚の並置構成
- **Context**: Requirement 1（ルート UIDocument）と Requirement 3（3 タブ UIDocument のプリロード）の両立。
- **Alternatives Considered**:
  1. TabView 標準コントロール — 却下（上記評価）
  2. シングル UIDocument + 動的 clone — 却下（UI-2 違反）
- **Selected Approach**: 1 つの PanelSettings（targetDisplay=0）を全 UIDocument で共有し、タブバーは独立 UIDocument（sortingOrder=0）、各タブは UIDocument（sortingOrder=1..3）として常駐配置。タブ切替は各タブ UIDocument の `rootVisualElement.style.display` を `Flex` / `None` で切り替える。
- **Rationale**: UI-1 / UI-2 を最小限のコードで表現でき、タブ spec の asmdef 分離（UI-4）も直感的に実現できる。
- **Trade-offs**: タブ UIDocument 4 枚分のメモリを常時確保する（非アクティブでも 3 枚が display:none で存在）。計測上 VisualTree のノード数 × 数 KB オーダーで、現代の配信 PC メモリ（16GB+）では無視できる。
- **Follow-up**: プリロード完了時刻を診断ログに出し、実機環境で 1 秒以内に収まっているか計測する（Requirement 11.1）。

### Decision: 非同期ロード基盤は Addressables Facade として実装
- **Context**: Requirement 4 の実装方針。UI-6 で Addressables 採用済み。
- **Alternatives Considered**:
  1. AssetBundle API 直接公開 — 却下（UI-6 違反、学習コスト高）
  2. UniTask / Task ベースの API — 将来検討余地あり（下記）
- **Selected Approach**: `IAsyncAssetLoader` インタフェースを抽象として定義し、実装は `Addressables.LoadAssetAsync<T>` の `AsyncOperationHandle.Completed` を購読して Completion コールバック（`Action<Result<T, LoadError>>`）として転送する。内部で scopeId 単位のハンドル集合を管理し `ReleaseAll(scopeId)` でまとめて解放する。
- **Rationale**: UI-6 の標準化方針に沿い、Addressables の成熟した機能（参照カウント・CDN 対応）を活かしつつ、シェル層で型安全な Result 型契約を提供できる。
- **Trade-offs**: Task ベースの API が使えない代わりに、コールバック式で Unity ライフサイクル（OnDisable 時のキャンセル等）と明示的に連動させる。
- **Follow-up**: UniTask 統合は本 spec のスコープ外。タブ spec が必要であれば自身で UniTask.AwaitForCompletion を被せる拡張を行える（シェル層は強制しない）。

### Decision: 接続未確立時の Command 送信は即時エラー返却
- **Context**: Requirement 9.4 の振る舞い確定（R-5）。
- **Alternatives Considered**:
  1. 保留キュー方式 — 却下（state coalesce の時間軸崩れ、古い値遅延反映）
  2. 呼び出し側ブロック — 却下（UI スレッドブロック禁止、Requirement 2.9）
- **Selected Approach**: Command 送信 API は `Result<SendOk, SendError>` を即時返却する。`SendError.NotConnected` は接続未確立を意味し、タブ spec は必要に応じて送信を諦めるかローカル UI 表現に留める。
- **Rationale**: state コマンドの coalesce 契約と両立し、UI スレッドをブロックしない。タブ spec の実装者は `IConnectionStatus.IsConnected` を参照して事前チェックできる。
- **Trade-offs**: タブ spec 側で送信失敗時のリカバリロジックが必要（UX としては「接続確立を待って操作を再実行」になる）。
- **Follow-up**: タブ spec 実装時に、`IConnectionStatus` の状態変化イベントを購読して UI 側のコントロール活性状態を追従させる実装を推奨する。

### Decision: スキン差し替え拡張点は ScriptableObject ベース
- **Context**: Requirement 6.4 の UXML 差し替え拡張点（R-3）。
- **Alternatives Considered**:
  1. Addressables 名前付き解決 — 却下（プリロード契約と非同期性の相性が悪い）
  2. 名前付き Resources.Load — 却下（パス結合・命名衝突リスク）
- **Selected Approach**: `UiToolkitShellSkinProfile`（ScriptableObject）を 1 つ定義し、タブバー / 3 タブそれぞれの `VisualTreeAsset`、`StyleSheet[]` への参照フィールドを持たせる。既定スキンはパッケージ内同梱、差し替えは利用者が別の ScriptableObject を PanelSettings / ShellBootstrapper のフィールドにドラッグ&ドロップで行う。
- **Rationale**: Unity Editor からの差し替え UX が直感的で、Inspector で差分が即確認できる。起動時プリロード（UI-1）とも相性が良い（同期読み込み可能）。
- **Trade-offs**: 利用者プロジェクト側で ScriptableObject アセットを 1 枚作る必要がある（手順 1 ステップ）。
- **Follow-up**: サンプルプロジェクトに「既定スキン差し替え」の手順を示す最小 ScriptableObject を同梱する。

### Decision: USS セレクタ命名規約は BEM 風の `vsb-` プレフィクス
- **Context**: UI-3 の「安定 USS セレクタ」と R-2 の具体化。
- **Alternatives Considered**:
  1. プレフィクスなし単純クラス名 — 却下（他 UI ライブラリとの衝突リスク）
  2. タグ / ID セレクタ — 却下（スキン差し替え効率低）
- **Selected Approach**: `vsb-` プレフィクス + BEM 風（`block__element--modifier`）。例：`vsb-tab-bar__button`、`vsb-tab-bar__button--active`、`vsb-tab-content`、`vsb-slider__handle`。
- **Rationale**: VTuberSystemBase の頭字語 `vsb` で衝突を回避、BEM の階層構造がスキン側の拡張に一貫性を与える。
- **Trade-offs**: 命名が冗長化するが、スキン互換性の恩恵が上回る。
- **Follow-up**: 共通 UI コンポーネントライブラリ（Requirement 7）と各タブ spec（#4〜#6）もこの規約に従う。規約違反検出（Requirement 6.5）は起動時に `rootVisualElement` を走査して必須クラスの存在を確認する。

### Decision: 初期アクティブタブは Character Selection
- **Context**: R-7 の既定値確定。
- **Selected Approach**: `ShellBootstrapper` の設定フィールド（既定値 = `Character`）で変更可能。設定ファイルからも読み込める（将来拡張）。
- **Rationale**: 配信準備の自然な順序。

### Decision: プリロード失敗時は手動リトライ UI を設けない
- **Context**: R-8 の確定。
- **Selected Approach**: 失敗タブは非活性保持、通知バーに警告テキスト、診断ログ記録のみ。
- **Rationale**: UXML パースエラーは再試行で復旧しないため、リトライ UI を備える価値が低い。運用時のスキン差し替え失敗は利用者が ScriptableObject を修正→PlayMode 再起動で解決するのが自然。

## Risks & Mitigations

- **R-1 プリロード所要時間**: 1 秒以内を目標。診断ログで実測監視。超過した場合は VisualTreeAsset のノード数削減やタブ spec の初期化処理分割を検討。
- **R-2 USS セレクタ命名の長期安定性**: `vsb-` プレフィクス + BEM 風で固定。規約変更は SemVer の major 相当として扱う。タブ spec・共通コンポーネント・スキン差し替えガイドに明示する。
- **R-3 UXML 差し替えの必須要素欠落**: 起動時に必須クラス名を走査検証し、欠落があれば該当タブのみ非活性化。他タブとシェル全体の起動は継続（Requirement 6.6 の直接適用）。
- **R-4 重複ロード抑止**: Addressables 内蔵の参照カウントに委譲。シェルは同一 key の未完了ハンドルへの追加購読だけ実装。Release 漏れを防ぐため scopeId 単位の `ReleaseAll` を提供。
- **R-5 接続未確立時の送信**: 即時エラー返却で固定。タブ spec は `IConnectionStatus` 参照で事前チェック推奨。
- **R-6 Display 1 フォールバック時の UI 警告**: シェル側通知バーに常時表示バッジ + 診断領域の詳細。メイン出力側から `PublishState` トピックで状態変化を受信し即時更新。
- **R-7 / R-8**: 上記 Decisions で確定。
- **R-9 （新規）PanelSettings 1 本共有時の入力フォーカス競合**: 3 タブ UIDocument のうち非アクティブが `display:none` でもフォーカス管理は残る可能性がある。Requirement 2.3 の表示切替処理の直後に `rootVisualElement.Focus()` を明示呼び出しして担保する。
- **R-10 （新規）タブ切替イベント購読解除漏れ**: タブ spec が OnDisable で購読解除を怠るとリスナーリーク。シェルは `ITabLifecycleToken`（IDisposable）を発行し、Dispose パターンで解除漏れを構造的に防ぐ。

## References

- [Panel Settings properties reference (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Runtime-Panel-Settings.html) — targetDisplay / sortingOrder の仕様
- [PanelSettings Scripting API (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.PanelSettings.html) — PanelSettings 公開 API
- [Configure runtime UI (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-render-runtime-ui.html) — ランタイム UI 構成手順
- [Panels (Unity 6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-panels.html) — 複数 UIDocument の同一パネル共有挙動
- [UI Document component (Unity 6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/UIE-create-ui-document-component.html) — UIDocument の OnEnable / OnDisable ライフサイクル
- [Create a tabbed menu (Unity 6000.2)](https://docs.unity3d.com/6000.2/Documentation/Manual/UIE-create-tabbed-menu-for-runtime.html) — TabView 標準コントロール（採用検討）
- [Asynchronous operation handles (Addressables 2.0)](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/AddressableAssetsAsyncOperationHandle.html) — AsyncOperationHandle.Completed のスレッド契約
- [Synchronous loading (Addressables 2.7)](https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/SynchronousAddressables.html) — WaitForCompletion 禁止の根拠
- [LoadAssetsAsync callback vs Task completion (Unity Forum)](https://forum.unity.com/threads/in-loadassetsasync-foo-is-there-a-reason-to-prefer-the-callback-function-over-the-task-completion.702440/) — コールバック設計比較
- [Multiple UIDocuments in one Scene (Unity Discussions)](https://discussions.unity.com/t/multiple-uidocuments-in-one-scene/820369) — 複数 UIDocument 並置の実運用事例
- [How To Switch Between UI Documents (Unity Discussions)](https://discussions.unity.com/t/how-to-switch-between-ui-documents/864879) — display 切替パターン
- `.kiro/specs/core-ipc-foundation/requirements.md` — 上流 D-1〜D-11 の継承元
- `.kiro/specs/output-renderer-shell/requirements.md` — OR-1 の横展開（Display 1 フォールバック警告）
- `.kiro/specs/character-selection-tab/requirements.md` — 本シェルの消費側 Requirement 1
- `.kiro/specs/stage-lighting-volume-tab/requirements.md` — 本シェルの消費側 Requirement 1
- `.kiro/specs/camera-switcher-tab/requirements.md` — 本シェルの消費側 Requirement 1
