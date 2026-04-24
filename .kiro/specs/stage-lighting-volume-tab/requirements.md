# Requirements Document

## Project Description (Input)
stage-lighting-volume-tab

ステージデータの読み込み、Light の動的管理、Global Volume 設定による「画作り」を行う UI タブを提供する spec。引きのカメラ 1 点で全体ルックを確認する。

スコープ:
- SceneViewStyleCameraController を用いた引きカメラの UI 組み込み
- ステージアセットの読み込み／切替 UI
- Light の動的生成・削除 UI（任意個数）
- 各 Light の角度・色・強さ・Type・Range 等を調整する GUI
- Global Volume の各 Override（Bloom, Tonemapping, ColorAdjustments 等）を編集する GUI
- 設定状態を IPC で output-renderer-shell に送信しメイン出力側へ反映
- Light 構成・Volume 設定の永続化（形式は設計フェーズで確定）

非目標:
- ステージアセットそのもの（利用者側の責務）
- 複雑なライティングプリセット管理（単純な保存・復元まで）
- 他タブ（キャラクター選択、カメラ）の機能
- UI Toolkit シェル基盤そのもの（spec #3 に分離）

対応要件: docs/requirements.md の §4.1, §5.2
上位計画: docs/spec-breakdown.md の spec #5（Wave 3、output-renderer-shell と ui-toolkit-shell に依存）

上流決定の継承:
- D-1: 単一 Unity アプリ + LocalHost ループバック
- D-3: 受信コールバックは Unity メインスレッド
- D-4: UI 側クライアント / メイン出力側サーバ
- D-10: PublishState / PublishEvent の使い分け
- OR-2: state 競合は last-write-wins
- UI-6: 追加アセットは Addressables 経由
- UI-7: タブ共通 UI 状態は永続化しない（タブ固有設定は本 spec の責務）
- CS-12 に準ずるプリセット設計: キャラクター選択タブと同様に、名前付きプリセットを複数保持する方式を本タブでも採用する方向で検討する

採用パッケージ: https://github.com/Hidano-Dev/SceneViewStyleCameraController
- Unity Editor の Scene ビュー相当のカメラ操作（マウス回転・パン・ズーム）

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec（`core-ipc-foundation`, `output-renderer-shell`, `ui-toolkit-shell`）の決定（D-1〜D-11, OR-1, OR-2, UI-1〜UI-7）およびキャラクター選択タブの CS-12（名前付きプリセット）は暗黙に継承される。本 spec 固有の暗黙デフォルトは以下の通り。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| SL-1 | 引きカメラ（プレビュー）の表示経路 | **SceneViewStyleCameraController による引きカメラはタブ UI 内に埋め込みの RenderTexture プレビューとして表示する**。メイン出力側の出力カメラではなく、UI 側（Display 1）の UIDocument 内パネルに描画する（ただしプレビューカメラはメイン出力側シーン内に存在し、メイン出力シーンの GameObject を映す点に注意）。メイン出力側の出力カメラ切替は本 spec のスコープ外（spec #6 camera-switcher-tab の責務）。プレビュー内容は SL-13 に従いステージ・ライティング・キャラクターすべてを描画対象とする。 | docs/requirements.md §5.2.2 の「引きのカメラ 1 点で全体ルックを調整」の UX は、メイン出力（配信に載る）とは独立した UI 上のプレビューであるべき。配信中にオペレーターが引きカメラを動かしても配信映像に影響しない分離が構造的に必要。 | 中（プレビュー用カメラがメイン出力のシーンをレンダリングする必要があり、メイン出力描画への GPU 負荷影響を設計フェーズで測定する必要あり） |
| SL-2 | Light GameObject の所有者 | **Light GameObject（Unity の Light コンポーネント）はメイン出力側のシーンに常駐し、所有権もメイン出力側にある**。UI 側は Light 識別子（lightId）を握り、生成・削除・プロパティ変更コマンドを IPC 経由で送信するのみ。UI 側で Light GameObject を直接生成・破棄・参照しない。 | docs/requirements.md §3.2 の「UI から直接メイン出力側オブジェクトを触らない」契約と整合。CS-1 と同一方針で、将来の LAN/WebUI クライアントが同じ IPC 契約で Light を操作可能（D-4）。 | 低 |
| SL-3 | Light 識別子の採番 | **Light 識別子（lightId）はメイン出力側が採番し、`PublishEvent` による Light 追加完了通知に含めて UI 側へ返す**。UI 側は「追加要求を送る → lightId 付き完了イベントを受信 → 以降の state 送信に lightId を使用」というフローを採用する。 | UI 側と他クライアント間で採番衝突を避けるため、採番権限をメイン出力側（権威ある状態所有者、D-4）に集中させる。UUID 等を UI 側で生成する案もあるが、サーバ側採番の方が一貫性が高く、将来の複数クライアント運用でもロジックが単純。 | 中（追加要求と lightId 発行の間に短い待ち時間が発生する。UI 上は「追加中」表示で吸収する） |
| SL-4 | ステージアセットの識別 | **ステージは Addressables の安定アドレス（string key）で識別する**（CS-4 と同一方針）。UI 側はステージ候補一覧（key + 表示名 + 任意のサムネイル）を IPC から取得し、切替操作は `PublishEvent`（離散操作）でメイン出力側へ送る。ステージ本体の Instantiate／破棄はメイン出力側で行う。 | UI-6 により追加アセットは Addressables 経由。キャラクター選択タブ（CS-4）と命名・識別規約を揃えることで、利用者プロジェクトの Addressables Groups 管理が一本化される。 | 中（Addressables key 命名規約は利用者側の責務。規約違反は診断ログで検出する） |
| SL-5 | Light 追加・削除のコマンド種別 | **Light の追加（add）／削除（remove）は離散操作であり `PublishEvent`（FIFO）で送信する**。Light ごとのプロパティ（angle/color/intensity/type/range/spotAngle 等）の変更は `PublishState`（coalesce 対象）で送信する。 | D-10 と CS-6 の直接反映。スライダーのドラッグ操作等の連続値は中間値を coalesce できるが、Light の生成・削除を漏らすと UI とメイン出力のシーン構成がずれるため FIFO で厳密に送る。 | 低 |
| SL-6 | state / event のトピック粒度 | **Light のプロパティは `light/{lightId}/{property}` 単位（例: `light/{lightId}/intensity`, `light/{lightId}/color`, `light/{lightId}/rotation`）に細分化する**。Volume Override のパラメータは `volume/override/{overrideType}/{param}`（例: `volume/override/bloom/intensity`）単位に細分化する。Override の有効／無効は `volume/override/{overrideType}/enabled`（state）として扱う。Light と Volume の discrete 操作（add/remove/enable/disable/preset-apply 等）は `light/command`, `volume/command` 等のイベント用集約トピックに寄せる。ステージ切替は `stage/command`（event）、現在ステージ ID は `stage/current`（state）。 | CS-7 と同一方針。プロパティ単位で coalesce を効かせると、別プロパティの中間値を巻き込まず UI の応答性が保たれる。event は種類が少ないため payload で種別を区別する集約トピックに寄せる。 | 中（トピック命名の具体規約は設計フェーズで確定。命名規約が後から変わると互換性影響が出る） |
| SL-7 | Volume Override の編集モデル | **URP Volume Override は「Override 種別（Bloom, Tonemapping, ColorAdjustments 等）」をリストとして管理し、各 Override は `enabled` 状態と、複数の `param`（VolumeParameter<T>）を持つ**。UI 側は利用可能な Override 種別を IPC から取得し、ユーザーが有効化した Override のパラメータを動的 UI（CS-5 と同様の動的生成）で編集する。`override` の `enabled` は state、`param` の値も state、Override 種別の有効化／無効化の切替は state（`enabled` トピック）として扱う。 | docs/requirements.md §5.2.5 の「各 Override を GUI から編集」を実現するためには、Override 種別ごとに固定 UI を書くのではなくメタデータ駆動で動的に UI を生成する方が拡張性が高い。URP の VolumeComponent / VolumeParameter の公開メタデータが活用できる。 | 中（Volume Override メタデータ取得 API をメイン出力側に用意する必要あり。設計フェーズで URP 公開 API の棚卸しが必要） |
| SL-8 | プリセット設計 | **「ステージ ID + Light 構成（配列） + Volume Override 構成」を 1 単位として名前付きプリセットを複数保持する（CS-12 準拠）**。本タブは `create / rename / duplicate / delete / activate` のプリセット CRUD UI と、アクティブプリセットの表示を提供する。プリセット切替時は通常の state/event コマンド経路（SL-5, SL-6）で一括適用し、メイン出力側では特殊ハンドラを用意しない。 | CS-12 と同一構造。配信シーンごと（朝配信、夜配信、コラボ配信等）の「画作り」テンプレートを 1 ボタンで切り替えられる。キャラクター選択タブと UX を揃えることでオペレーターの学習コストも下がる。 | 中（Light 配列を「一括再構築」するため、切替時に既存 Light の remove と新規 Light の add を多数送ることになる。設計フェーズで整合性維持のための送信順序と中間状態の扱いを確定する） |
| SL-9 | プリセット永続化のタイミング | **プリセット内容の変更（Stage/Light/Volume のいずれかの state 変化、および Light add/remove）があった都度、デバウンス（具体値は設計フェーズで確定）後にアクティブプリセットのファイルへフラッシュする**。アプリ終了時にも保留中をフラッシュする（CS-9 と同一方針）。 | 配信現場では「保存忘れ」が配信事故に直結する。CS-9 と揃えることで UX 一貫性を得る。 | 中（Light 追加直後はまだ lightId が未発行の場面があり、保存対象のエッジケース整理が必要） |
| SL-10 | 引きカメラの状態は永続化しない | **引きカメラ（プレビュー）の位置・回転・ズーム等の状態は永続化対象外**。毎回タブ初期化時にデフォルト視点から開始する。 | 引きカメラは「画作り作業用の一時的な視点」であり、配信映像ではない。永続化すると前回の視点が次回起動時に不要に残り、「ステージ全体を俯瞰したい」という初期 UX を損なう可能性がある。 | 低 |
| SL-11 | ステージ Addressables ロード失敗時の挙動 | **ステージ切替要求に対しメイン出力側から失敗イベントが返った場合、UI 側は「切替失敗」状態を UI 上に表示し、直前の（切替前の）ステージ状態を維持する**。保存プリセットが壊れたステージ key を参照していた場合は、ステージ選択を「未選択」状態に落とし、警告バッジを表示する（CS-11 と同一思想）。 | 配信直前のアセット差し替え事故に対して、サイレントな誤ステージ表示を発生させない。他の Light / Volume 設定は独立して復元可能な状態を保つ。 | 中（「切替失敗」と「未選択」の UX は設計フェーズで確定） |
| SL-12 | パラメータバリデーションの範囲 | **UI 側では URP / Unity が公開する値範囲（Intensity >= 0、Color の 0.0〜1.0、Range >= 0 等）を一次バリデーションし、範囲外は送信抑止する**。厳密な検証はメイン出力側で行い、エラー時は event で UI へ返す。 | D-11（1 MB 上限）とは別に、個別プロパティのレンジ違反を UI 側で捕捉することで IPC の往復を節約し、UX を改善する。CS-5 の設定値バリデーション方針と整合。 | 低 |
| SL-13 | 引きカメラプレビューの映す内容 | **ステージ + ライティング + キャラクター（アバター）すべてを映す**。プレビュー用カメラのカリングマスクをメイン出力カメラと同等に設定し、実配信と同じ見た目でルックを調整できるようにする。メイン出力側シーンを 2 視点（メイン出力本番カメラ + プレビューカメラ）で同時描画する形となる。 | 「画作り」の評価は最終的に「キャラが乗った状態でどう見えるか」が基準。ステージのみでライティング調整しても、アバターの肌・髪のシェーダーへの影響が確認できず二度手間になる。 | 中（プレビューカメラの追加で GPU 負荷がメイン出力本番カメラに加算される。設計フェーズで解像度・リフレッシュレート・描画頻度の最適化方針を決定する） |
| SL-14 | Light 生成数の上限 | **上限を設けない（警告も出さない）**。オペレーターが任意個数の Light を生成・配置できる。URP のフォワードレンダリング上限を超えて配置した場合に影響を受けない Light が発生する可能性は、利用者側の運用責任とする。 | 利用者プロジェクトごとに必要な Light 数は大きく異なる（小規模ステージと大規模ステージでは 10 倍差もあり得る）。固定上限は運用の柔軟性を損なう。警告表示の設計も利用者のワークロードによっては邪魔になる。 | 中（配信中に気づかず多数の Light を設置して fps が落ちるリスクは残る。診断 API で現在 Light 数を露出することで、運用側で監視可能にする。将来のパフォーマンスプロファイラ spec で fps 監視を導入する候補） |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **ステージ・ライティング・Volume タブ（Stage-Lighting-Volume Tab）** を定義する。Display 1 側の UI Toolkit シェル（spec #3 `ui-toolkit-shell`）内に配置される 3 タブのうち 1 つとして、配信前の「画作り」フェーズでシーン全体のルックを確定する責務を持つ。具体的には：

1. **引きカメラ（プレビュー）の UI 埋め込み**：[SceneViewStyleCameraController](https://github.com/Hidano-Dev/SceneViewStyleCameraController) を用いたマウス回転・パン・ズーム操作が可能な引きカメラのプレビュー映像を、**タブ UI 内に埋め込んだ RenderTexture パネル** として表示する。これは **配信に載るメイン出力カメラとは別のプレビュー専用カメラ** であり、Display 1 の UI 内に閉じる（SL-1）。
2. **ステージアセットの読み込み／切替 UI**：Addressables 経由で登録されたステージ候補の一覧を提示し、現在のステージ切替・確認をオペレーターに許可する（SL-4）。切替操作は `PublishEvent`、現在のステージ ID は `PublishState` として IPC で伝搬する（SL-5, SL-6）。
3. **Light の動的管理 UI**：任意個数の Light を実行中に追加・削除でき（SL-5）、各 Light の Type / 角度（Transform Rotation）/ 色 / 強さ / Range / Spot Angle 等をプロパティ単位の `PublishState` トピックで編集する（SL-6）。Light GameObject の所有権はメイン出力側にあり、UI 側は lightId と編集コマンドのみ扱う（SL-2, SL-3）。
4. **Global Volume の Override 編集 UI**：URP の Volume Override（Bloom, Tonemapping, ColorAdjustments 等）について、各 Override の `enabled` と `param` 値をメタデータ駆動で動的 UI 生成し、state コマンドで編集する（SL-7）。
5. **IPC 契約に基づく状態同期**：連続値（Light プロパティ、Volume Override パラメータ）は `PublishState`、離散操作（Light add/remove, Stage 切替, Volume Override enable/disable, プリセット適用等）は `PublishEvent` で送信する（D-10, SL-5, SL-6）。
6. **名前付きプリセットの永続化と復元**：ステージ + Light 構成 + Volume 構成を 1 単位とする名前付きプリセットを複数保持し、CRUD / アクティブ化 UI を提供する（CS-12 準拠、SL-8）。デバウンス即時保存（SL-9）・通常 state/event 経路での復元（CS-10 準拠）により、メイン出力側のハンドラを 1 本化する。
7. **失敗ハンドリング**：Addressables ロード失敗、範囲外パラメータ、メイン出力側からのエラー event 等について、UI 側で安全に縮退し、メイン出力描画に波及させない（SL-11, SL-12）。

本 spec は **UI 側のタブ機能** に限定される。メイン出力シーンでの Light GameObject 生成・破棄・Volume コンポーネント操作・ステージ Prefab Instantiate、IPC トランスポートそのもの、UI Toolkit シェル基盤、メイン出力シーン骨格、他タブの機能、**メイン出力カメラ（配信に載るカメラ）の操作・切替**、ステージアセット本体のデザインは本 spec の責務外である（SL-1, SL-2 参照）。

## Boundary Context

- **In scope**:
  - 本タブの UIDocument（VisualTreeAsset + StyleSheet）と、`ui-toolkit-shell` の UXML/USS 配置規約（UI-3, UI-4）への適合
  - SceneViewStyleCameraController を用いた **引きカメラ（プレビュー）** のタブ UI 内埋め込み（RenderTexture 経由のプレビューパネル表示、SL-1）
  - 引きカメラ操作 UI（マウス回転・パン・ズーム等）の露出および視点リセット操作
  - ステージ候補一覧 UI（Addressables 由来、SL-4）とステージ切替／解除操作 UI
  - Light 一覧 UI（追加・削除・選択・複製等の操作）と、選択 Light の編集パネル（angle / color / intensity / Type / Range / Spot Angle 等）
  - Global Volume の Override リスト UI（Bloom, Tonemapping, ColorAdjustments 等）と、Override ごとの `enabled` トグル・param 編集用動的 UI（SL-7）
  - 連続値設定の `PublishState` 送信、離散操作の `PublishEvent` 送信、必要な Request/Response（Volume Override メタデータ取得等、SL-5, SL-6, SL-7）
  - メイン出力側からの state / event / response の購読（Light 追加完了通知・lightId 発行、ステージロード完了／失敗、Volume メタデータ応答、RAC 同様の診断イベント等）
  - 名前付きプリセット（Stage + Lights + Volume）CRUD UI とアクティブプリセット表示、プリセット切替（SL-8, CS-12）
  - プリセット内容の永続化（デバウンス即時保存）と起動時復元（通常 state/event 経路）
  - 不可用ステージ・範囲外値・メイン出力エラーの縮退挙動（SL-11, SL-12）
  - タブのアクティブ化／非アクティブ化時の購読登録／解除、および非アクティブ時の引きカメラ描画一時停止（`ui-toolkit-shell` Requirement 2 Acceptance Criteria 8 の拡張点を利用）
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 の継承）
- **Out of scope**:
  - メイン出力シーンでの **Light GameObject の実生成・破棄・プロパティ適用** 実装（メイン出力側の responsibility。本 spec は IPC 契約のみ定義、SL-2）
  - メイン出力シーンでの **ステージ Prefab の Instantiate / Unload** 実装（メイン出力側の responsibility、SL-4）
  - メイン出力シーンでの **URP Volume Component への値適用** 実装（メイン出力側の responsibility）
  - ステージアセット本体のデザイン（利用者プロジェクトの責務）
  - **メイン出力カメラ（配信に載るカメラ）の操作・切替**（spec #6 camera-switcher-tab の責務）
  - **配信中のカメラ切替時のトランジション**（spec #6 の非目標の範囲に整合）
  - `core-ipc-foundation` のトランスポート・シリアライゼーション（spec #1 の責務）
  - `ui-toolkit-shell` のルート UIDocument・タブ切替機構・Command 送信 API 実装・非同期ロード基盤実装（spec #3 の責務）
  - `output-renderer-shell` のシーン初期化・ディスパッチャ・デフォルトライト／空 Global Volume の配置（spec #2 の責務。本タブはそこに Light / Volume Override を差し込む側）
  - 他タブ（キャラクター選択、カメラスイッチャー）の機能
  - プログラムされたライティングトランジション（フェードイン、時間変化、タイムライン制御等、本 spec の非目標）
  - MoCap / アクターの操作（キャラクター選択タブの責務）
  - タブ共通 UI 状態（アクティブタブ、ウィンドウサイズ等）の永続化（UI-7 により永続化しない）
- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）の抽象インタフェースが利用可能で、`PublishState` / `PublishEvent` / `Request` / 受信購読が Unity メインスレッド配信で使えること（D-3, D-10）
  - `ui-toolkit-shell`（spec #3）が Command 送信 API・受信購読 API・共通 UI コンポーネントライブラリ・非同期ロード基盤・タブ配置規約を公開していること（UI-4, UI-5, UI-6）
  - `output-renderer-shell`（spec #2）が、本 spec で定義する state / event / request の topic を受け付けるハンドラを登録可能な構造であること。特に空の Global Volume（OR Requirement 1.5）と、Light ルート／Stage ルート GameObject の存在（OR Requirement 1.1）を前提とする
  - 採用パッケージ SceneViewStyleCameraController が Unity 6.3 で利用可能であり、カメラ制御コンポーネントを任意の Camera に取り付けて RenderTexture 出力を得られること
  - 利用者プロジェクトが Addressables Groups で必要なステージアセットを登録・ビルドしていること（SL-4）
  - メイン出力側（`output-renderer-shell` の拡張またはアダプタ層）が、本 spec の IPC 契約に従って Light / Stage / Volume を操作する実装を別途提供すること（本 spec はその相手方の存在を前提とした UI 側契約のみを定義する）

---

### Requirement 1: ステージ・ライティング・Volume タブ UIDocument の配置と UI Toolkit シェル統合

**Objective:** タブ spec の開発者として、本タブを `ui-toolkit-shell` の 3 タブのうち 1 枠に正しく載せ、起動時一括プリロード・表示/非表示切替のみのタブ遷移・メイン出力を 1 フレームもフリーズさせない要件を満たす形で統合したい。そうすればタブ独自のロード戦略を書かずに済み、シェル側の契約で性能要件を構造的に担保できる。

**Note:** 本要件は `ui-toolkit-shell` の Requirement 1（ルート UIDocument）・Requirement 2（タブ切替）・Requirement 3（起動時一括プリロード）の契約を受け入れる側の責務を定義する。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 本タブ専用の UIDocument（VisualTreeAsset）および StyleSheet を、`ui-toolkit-shell` が定義する UXML/USS 配置規約（UI-3 相当）に従って提供する。
2. The Stage-Lighting-Volume Tab shall 本タブのルート要素および主要要素に対して、`ui-toolkit-shell` の USS セレクタ命名規約（クラス名プレフィクス等）を適用し、スキン差し替え経路（UI-3）から見た目を変更可能にする。
3. When `ui-toolkit-shell` が起動時プリロードを実行したとき、the Stage-Lighting-Volume Tab shall 本タブの VisualTreeAsset・StyleSheet を同期的にアタッチ完了させ、タブ切替時に再 clone や再生成を発生させない（see UI-1, UI-2）。
4. When 本タブがアクティブ化されたとき、the Stage-Lighting-Volume Tab shall USS の `display` / `visible` プロパティによる表示化のみで UI を提示し、VisualTreeAsset の再ロード・メインスレッドブロッキング処理を行わない（see UI-2）。
5. When 本タブが非アクティブ化されたとき、the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` が公開するタブ切替イベントに応じて、引きカメラのプレビュー描画を一時停止し、購読解除やストリーミング UI の停止など必要な状態保存処理を行う（see Requirement 2）。
6. The Stage-Lighting-Volume Tab shall 本タブの UI 構築・表示切替処理において、メイン出力（Display 2+）の描画フレームに干渉する同期 I/O・メインスレッドブロッキング処理を一切含まない（see docs/requirements.md §4.2, §6.1）。
7. The Stage-Lighting-Volume Tab shall 本タブのアセンブリ定義（asmdef）を独立させ、`ui-toolkit-shell` の公開 API と `core-ipc-foundation` の抽象インタフェース、および採用パッケージ SceneViewStyleCameraController 以外に直接依存しない参照方向を維持する。
8. Where 利用者プロジェクトが本タブの UXML を差し替える場合, the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` の UXML 差し替え拡張点（UI-3）を経由させ、必須要素の欠落があれば診断ログへ記録する。

---

### Requirement 2: 引きカメラ（プレビュー）の UI 埋め込み

**Objective:** 配信オペレーターとして、本タブを開いている間、マウス回転・パン・ズームでステージ全体を俯瞰できるプレビュー画面がタブ UI 内に表示され、配信に載るメイン出力とは独立に視点を動かせる状態を得たい。そうすれば配信中でも配信映像に影響せずに「画作り」作業が行える。

**Note:** 本要件は SL-1 の決定に従い、引きカメラは **タブ UI 内の RenderTexture プレビュー**（Display 1 側）として実装する。配信に載るメイン出力カメラは spec #6 camera-switcher-tab の責務であり、本 spec では扱わない。SceneViewStyleCameraController を採用し、Unity Editor の Scene ビュー相当のカメラ操作（マウス回転・パン・ズーム）を提供する（docs/requirements.md §5.2.2）。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall **SceneViewStyleCameraController を用いた引きカメラ（プレビュー専用 Camera）** を UI 起動時に 1 つ用意し、その出力を **RenderTexture** に描画する（see SL-1）。
2. The Stage-Lighting-Volume Tab shall 当該 RenderTexture を本タブ UIDocument 内の **プレビューパネル**（UI Toolkit の `VisualElement` / `Image` 相当）に表示する。
3. The Stage-Lighting-Volume Tab shall 引きカメラおよびプレビュー RenderTexture を **Display 1（UI 側）にのみ描画** し、メイン出力サーフェス（Display 2+）には一切描画しない（see docs/requirements.md §6.2, OR Requirement 5）。
4. The Stage-Lighting-Volume Tab shall 引きカメラの操作（マウス回転・パン・ズーム）を SceneViewStyleCameraController の標準 UX に従って提供し、オペレーターはプレビューパネル上でインタラクトできる。
5. The Stage-Lighting-Volume Tab shall 引きカメラを **配信に載るメイン出力カメラとは別のカメラ** として実装し、本タブ内での視点操作がメイン出力カメラの状態に影響しないことを構造的に保証する（see SL-1）。
6. When 本タブが非アクティブ化されたとき、the Stage-Lighting-Volume Tab shall 引きカメラのプレビュー描画を一時停止し、不要な GPU リソース消費を抑制する（see Requirement 1 Acceptance Criteria 5）。
7. When 本タブが再アクティブ化されたとき、the Stage-Lighting-Volume Tab shall プレビュー描画を再開し、直前の視点状態を維持する（永続化はしない、SL-10）。
8. The Stage-Lighting-Volume Tab shall 引きカメラの **視点リセット操作**（デフォルト視点に戻す）を UI から実行可能な形で提供する。
9. The Stage-Lighting-Volume Tab shall 引きカメラの位置・回転・ズーム等の状態を永続化しない（see SL-10）。
10. The Stage-Lighting-Volume Tab shall プレビュー描画処理が **メイン出力（Display 2+）の描画フレームに干渉しない** ことを設計上保証する（レンダラ分離・低優先度スケジューリング等の具体手段は設計フェーズで確定）。
11. Where プレビュー RenderTexture がメイン出力シーンのオブジェクト（ステージ・Light・キャラクター等）をレンダリングする必要がある場合, the Stage-Lighting-Volume Tab shall メイン出力シーンの共有利用方法（共有シーン／別シーン／レイヤー分離等）を設計フェーズで確定し、メイン出力カメラとの描画競合が生じない配置とする。

---

### Requirement 3: ステージアセットの読み込みと切替

**Objective:** 配信オペレーターとして、利用可能なステージアセットの一覧から目的のステージを選んで切り替え、メイン出力とプレビューに反映される体験を得たい。そうすれば配信シーンごとに異なるステージを素早く差し替えられる。

**Note:** ステージ識別は Addressables の安定 key で行う（SL-4）。実アセットの Instantiate / Unload はメイン出力側の responsibility で、UI は識別子と切替操作のみ扱う。切替は離散操作として `PublishEvent` で送信する（SL-5, SL-6）。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 利用可能なステージ候補の一覧（Addressables key、表示名、任意でサムネイル相当のメタデータ）をメイン出力側から **Request/Response** または **state** で取得する（具体方式は設計フェーズで確定、topic 例: `stage/catalog`）。
2. The Stage-Lighting-Volume Tab shall 取得したステージ候補を **ステージ選択 UI**（リストまたはグリッド）として表示し、各項目に表示名を可視化する（see SL-4）。
3. Where 各ステージにサムネイルが同梱されている場合, the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` の非同期ロード API（UI-6）経由でサムネイルを取得し、候補 UI に表示する。サムネイル未設定時はデフォルト画像にフォールバックする。
4. When オペレーターがステージ候補から 1 つを選択し切替を確定したとき、the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` の Command 送信 API を介して **event コマンド**（topic 例: `stage/command`、payload に操作種別 `load` と Addressables key を含む）を送信する（see SL-5, SL-6）。
5. When オペレーターが現在のステージを解除（unload / 未選択状態）したいとき、the Stage-Lighting-Volume Tab shall **event コマンド**（payload で `unload`）を送信する。
6. The Stage-Lighting-Volume Tab shall メイン出力側から購読する **現在のステージ ID state**（topic 例: `stage/current`）に基づいて、UI 上でアクティブなステージを可視化する（see SL-6）。
7. While ステージ切替要求の送信からメイン出力側の適用完了通知を待機している間, the Stage-Lighting-Volume Tab shall UI 上に進行中表示（スピナー等）を提示し、同一タイミングでの重複切替要求を抑止または直列化する。
8. If メイン出力側からステージロード失敗イベントが通知された場合, the Stage-Lighting-Volume Tab shall UI 上に切替失敗を提示し、**直前のステージ（切替前の状態）を維持** し、他の UI 要素（Light, Volume, プレビュー）の動作を継続させる（see SL-11）。
9. If ステージ候補一覧の取得に失敗した場合, the Stage-Lighting-Volume Tab shall ステージ選択 UI 上に再試行可能なエラー表示を提示し、本タブ全体および他タブの動作を阻害しない。
10. When ステージ候補 state が更新された（ステージ追加・削除）とき、the Stage-Lighting-Volume Tab shall 候補 UI を追従更新し、再起動を要求しない。
11. The Stage-Lighting-Volume Tab shall 同時にアクティブ化されるステージは高々 1 つとして扱い、切替要求は常に「unload 前ステージ → load 新ステージ」のセマンティクスで送信する（詳細な送信順序はメイン出力側の実装契約として設計フェーズで確定）。

---

### Requirement 4: Light の動的管理（追加・削除・識別）

**Objective:** 配信オペレーターとして、タブを開いている間に任意個数の Light をシーンに追加・削除し、各 Light を識別して個別に編集できる状態を得たい。そうすればリハーサル中・本番前の画作りで自在にライティングを詰められる。

**Note:** Light GameObject はメイン出力側に常駐し所有権もメイン出力側（SL-2）。UI 側は lightId を握り、追加／削除は `PublishEvent`、プロパティ変更は `PublishState` で送る（SL-5, SL-6）。lightId はメイン出力側が採番して追加完了イベントで UI 側に返却する（SL-3）。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall メイン出力側から購読する **Light 一覧 state**（topic 例: `lights/list`、payload に lightId 配列と各 Light のメタデータを含む）に基づいて、UI 上に現存する Light のリストを表示する。
2. The Stage-Lighting-Volume Tab shall Light リスト UI 上で各 Light を **lightId または表示名** で識別可能な形で列挙し、選択・編集・削除操作の起点を提供する。
3. When オペレーターが Light の追加操作（`Add Light` ボタン等）を実行したとき、the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` の Command 送信 API を介して **event コマンド**（topic 例: `light/command`、payload に操作種別 `add` と初期 Type／デフォルト値を含む）を送信する（see SL-5）。
4. When メイン出力側から Light 追加完了イベント（採番された lightId を含む）が通知されたとき、the Stage-Lighting-Volume Tab shall 当該 lightId を内部に保持し、UI の Light リストに反映する（see SL-3）。
5. When オペレーターが Light の削除操作を実行したとき、the Stage-Lighting-Volume Tab shall **event コマンド**（topic 例: `light/command`、payload に操作種別 `remove` と対象 lightId を含む）を送信し、UI リストから該当項目を（メイン出力側からの削除完了 state/event 通知後に）除去する。
6. While Light 追加要求の送信から lightId 付き完了イベントを待機している間, the Stage-Lighting-Volume Tab shall UI 上に「追加中」プレースホルダを表示し、同一タイミングでの複数回連打による重複追加を構造的に抑止する（see SL-3）。
7. If Light 追加イベントの送信から一定時間（設計フェーズで確定）完了イベントが届かなかった場合, the Stage-Lighting-Volume Tab shall 「追加中」プレースホルダをタイムアウト扱いにし、UI 上に警告を提示したうえでオペレーターが再試行できる状態に戻す（UI クラッシュを発生させない）。
8. The Stage-Lighting-Volume Tab shall Light 一覧の表示順を安定的な順序（例: lightId の採番順、または表示名昇順）で固定し、state 更新のたびに順序が揺らがないようにする。
9. While Light 一覧 state がまだメイン出力側から受信できていない間, the Stage-Lighting-Volume Tab shall Light リスト領域にプレースホルダまたは「接続待ち」表示を提示し、Light 操作を非活性化する。
10. If メイン出力側から Light 追加失敗イベント（例: リソース上限、内部エラー）が通知された場合, the Stage-Lighting-Volume Tab shall 「追加中」プレースホルダをエラー表示に切り替え、失敗事由を診断ログおよび UI 側診断領域で参照可能にする（see Requirement 10）。

---

### Requirement 5: Light プロパティの編集

**Objective:** 配信オペレーターとして、選択した Light の角度・色・強さ・Type・Range・Spot Angle 等を GUI スライダー・カラーピッカー等で調整し、結果がリアルタイムにメイン出力と引きプレビューに反映される体験を得たい。そうすれば「画作り」のイテレーションが高速に回る。

**Note:** 連続値は `PublishState`（coalesce 対象）で、トピックは `light/{lightId}/{property}` 単位で細分化する（SL-5, SL-6）。Type の切替は state として扱うが、Type 変更に伴う適用範囲（Spot 固有の Spot Angle 露出等）の整合性は UI 側で担保する。

#### Acceptance Criteria

1. When オペレーターが Light リストから 1 つを選択したとき、the Stage-Lighting-Volume Tab shall 当該 Light の編集パネルを UI 上で表示し、現在値（Type / 角度 / 色 / 強さ / Range / Spot Angle 等）を反映する。
2. The Stage-Lighting-Volume Tab shall 選択 Light の現在値をメイン出力側から購読する **プロパティ単位の state**（topic 例: `light/{lightId}/intensity`, `light/{lightId}/color`, `light/{lightId}/rotation`, `light/{lightId}/type`, `light/{lightId}/range`, `light/{lightId}/spotAngle`）を元に表示する（see SL-6）。
3. When オペレーターが Light プロパティ（色 / 強さ / 角度 / Range / Spot Angle 等）を変更したとき、the Stage-Lighting-Volume Tab shall 該当プロパティトピックに対して `PublishState` を送信する（see SL-5, SL-6）。
4. When オペレーターが Light の Type（Directional / Point / Spot / Area 等）を変更したとき、the Stage-Lighting-Volume Tab shall `light/{lightId}/type` トピックに `PublishState` を送信し、Type に応じた編集可能プロパティのみを UI 上で活性化する（例: Spot 時のみ Spot Angle と Range を活性化、Directional 時は Range / Spot Angle を非活性化）。
5. The Stage-Lighting-Volume Tab shall 高頻度連続値の変更（スライダーのドラッグ中等）に対して、`PublishState` の coalesce 特性（D-7）に任せることを前提に、UI 側での流量制御（スロットリング等）を最小限に留める。
6. While オペレーターが Light プロパティをドラッグ等で連続変更している間, the Stage-Lighting-Volume Tab shall メイン出力描画に干渉する同期処理を行わず、UI のレスポンス性を維持する。
7. The Stage-Lighting-Volume Tab shall Light プロパティの値範囲（Intensity >= 0、Color の 0.0〜1.0、Range >= 0、Spot Angle の 1〜179 度等の Unity / URP 規定範囲）を UI 側でバリデーションし、範囲外の送信を抑止する（see SL-12）。
8. When メイン出力側から Light プロパティ state が通知された（他クライアントからの変更や復元など）とき、the Stage-Lighting-Volume Tab shall UI 上の該当コントロール表示を当該値に追従更新する（ただしオペレーターが操作中のコントロールは設計フェーズで確定する方針で競合解消する）。
9. If メイン出力側から Light 関連のエラー event が通知された場合, the Stage-Lighting-Volume Tab shall 該当 Light のリスト項目および編集パネルにエラー表示を切り替え、他 Light の表示・操作は継続する。
10. The Stage-Lighting-Volume Tab shall 共通 UI コンポーネントライブラリ（UI-4）のスライダー・カラーピッカー・トグルグループ等を活用し、重複実装を避けて Light 編集 UI を構成する。
11. The Stage-Lighting-Volume Tab shall Light プロパティの変更が、プロパティ単位のトピック分割により **別プロパティの中間値を巻き込まずに coalesce される** ことを設計上の前提とする（see SL-6）。

---

### Requirement 6: Global Volume の Override 編集

**Objective:** 配信オペレーターとして、URP Global Volume の Bloom / Tonemapping / ColorAdjustments 等の各 Override を GUI から有効化・無効化し、各パラメータを調整できる体験を得たい。そうすればポストエフェクトを配信シーンに合わせて詰められる。

**Note:** Override メタデータ取得は Request/Response、Override の `enabled` と各 `param` は state（coalesce 対象）として扱う（SL-6, SL-7）。UI はメタデータから動的にコントロールを生成し、静的な UI を手書きしない（CS-5 と同一思想）。

#### Acceptance Criteria

1. When 本タブがアクティブ化されたとき、the Stage-Lighting-Volume Tab shall 利用可能な Volume Override 種別の一覧と、各 Override の **param メタデータ**（param 名・型・レンジ・既定値・表示名等）を Request でメイン出力側から取得する（topic 例: `volume/override/schema`、see SL-7）。
2. The Stage-Lighting-Volume Tab shall 取得したメタデータに従って、`ui-toolkit-shell` の共通 UI コンポーネントライブラリ（UI-4）から適切なコントロール（スライダー / カラーピッカー / トグル / 数値入力等）を **動的に生成** する（see SL-7）。
3. The Stage-Lighting-Volume Tab shall Global Volume の Override 一覧 UI を提供し、各 Override に対して **有効化トグル（enabled）** と、有効時の param 編集領域を表示する。
4. When オペレーターが Override の有効化トグルを切り替えたとき、the Stage-Lighting-Volume Tab shall `volume/override/{overrideType}/enabled` トピックに `PublishState` を送信する（see SL-6, SL-7）。
5. When オペレーターが Override の param 値を変更したとき、the Stage-Lighting-Volume Tab shall `volume/override/{overrideType}/{param}` トピックに `PublishState` を送信する（see SL-6）。
6. The Stage-Lighting-Volume Tab shall 高頻度連続値の param 変更（スライダードラッグ等）に対して `PublishState` の coalesce 特性に任せ、UI 側の流量制御を最小限に留める。
7. The Stage-Lighting-Volume Tab shall Override param の値範囲をメタデータに基づいて UI 側でバリデーションし、範囲外の送信を抑止する（see SL-12）。
8. When メイン出力側から Volume Override の enabled / param state が通知されたとき、the Stage-Lighting-Volume Tab shall UI 上の該当コントロール表示を当該値に追従更新する。
9. If Volume Override メタデータの取得 Request がタイムアウトまたは失敗した場合, the Stage-Lighting-Volume Tab shall Volume 編集領域にエラー表示を提示し、再試行 UI を提供したうえで、本タブ全体および他タブの動作を阻害しない。
10. If メタデータ内に未知の型・必須フィールド欠落等の不整合があった場合, the Stage-Lighting-Volume Tab shall 該当 param のみスキップして他 param の UI を提示し、診断ログに記録する。
11. Where 利用者プロジェクトが独自 VolumeComponent を追加している場合, the Stage-Lighting-Volume Tab shall メイン出力側が返すメタデータに従って追加 Override を UI 上に露出する（静的な Override 固定実装を避ける）。

---

### Requirement 7: 名前付きプリセットと一括適用（CS-12 準拠）

**Objective:** 配信オペレーターとして、「朝配信」「夜配信」「コラボ配信」等のシーンごとに、ステージ + Light 構成 + Volume 構成を 1 単位の名前付きプリセットとして保存・切替できる体験を得たい。そうすれば配信テンプレートを 1 ボタンで切り替えられ、毎日の準備時間が短縮される。

**Note:** プリセット設計は CS-12 と同一方針（SL-8）。プリセット切替時の適用は通常の state/event コマンド経路で一括送信し、メイン出力側に特殊ハンドラを用意しない。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 「ステージ ID + Light 構成（Light 配列と各 Light のプロパティ）+ Volume Override 構成（有効な Override と param 値）」を 1 単位とする **名前付きプリセット** を複数保持する（see SL-8, CS-12）。
2. The Stage-Lighting-Volume Tab shall オペレーターが以下のプリセット操作を UI から実行できるようにする：**新規作成（create）**、**名前変更（rename）**、**複製（duplicate）**、**削除（delete）**、**アクティブ化（activate/switch）**（see CS-12）。
3. The Stage-Lighting-Volume Tab shall 現在アクティブなプリセット名を UI 上に明示的に表示する。
4. When オペレーターがアクティブプリセットを切り替えたとき、the Stage-Lighting-Volume Tab shall 切替先プリセットの内容を通常の state / event コマンド経路（Requirement 3, 4, 5, 6）で送信し、メイン出力側のステージ / Light / Volume を一括適用する（see CS-10, CS-12, SL-8）。
5. If プリセット新規作成時に既存プリセットと重複する名前が指定された場合, the Stage-Lighting-Volume Tab shall 作成を拒否してバリデーションエラーを UI に表示する。
6. If プリセット切替中に一部の state / event 送信が失敗した場合, the Stage-Lighting-Volume Tab shall 失敗事由を診断ログに記録し、UI 上に部分適用警告を提示したうえで、他のコマンド送信は継続する（本 spec は部分適用のまま継続する方針。厳密なロールバック要件は設計フェーズで確定）。
7. The Stage-Lighting-Volume Tab shall プリセット切替時に、**現存する Light 群を一度 remove してから新プリセットの Light 群を add** するセマンティクス、または差分適用セマンティクスのいずれを用いるかを設計フェーズで確定し、構造的に整合性を維持する（see SL-8）。
8. Where プリセットに含まれる Light が依然として存在するセッション間遷移の場合, the Stage-Lighting-Volume Tab shall lightId の再採番が発生することを前提として UI の追従を組み立てる（採番はメイン出力側、SL-3）。
9. The Stage-Lighting-Volume Tab shall プリセット一覧・アクティブプリセット管理を UI 側の状態として保持し、メイン出力側へは個別の state / event として送信する（プリセット管理そのものをメイン出力側に伝送しない）。

---

### Requirement 8: プリセットの永続化と復元

**Objective:** 配信オペレーターとして、前回タブ終了時点のプリセット内容（アクティブプリセットと全プリセット）が、次回起動時に自動で復元される状態を得たい。そうすれば毎日の配信準備を短縮でき、作業途中の保存忘れによる事故も防げる。

**Note:** UI-7 により `ui-toolkit-shell` 自体は UI 状態を永続化しないが、タブ固有設定の永続化は本 spec の責務（Project Description 明記および CS-8 準拠）。保存タイミングは CS-9 と同一思想（デバウンス即時保存）、復元経路は CS-10 と同一（通常 state/event 経路）に従う。保存ファイルの配置・フォーマット（JSON / バイナリ等）は設計フェーズで確定する。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 永続化対象を **「全プリセット内容（ステージ ID + Light 構成 + Volume Override 構成）」** および **「アクティブプリセット名」** とし、プリセット単位で保存する（see SL-8, CS-8）。
2. The Stage-Lighting-Volume Tab shall 永続化対象外とするもの（引きカメラの視点状態、タブ切替状態、ウィンドウ配置、メイン出力側の Light GameObject インスタンス等）を保存ファイルに含めない（see SL-10, UI-7）。
3. When 永続化対象の値（アクティブプリセットのステージ / Light プロパティ / Light 追加・削除 / Volume Override）が変更されたとき、the Stage-Lighting-Volume Tab shall 変更を内部バッファに蓄積し、**デバウンス**（具体値は設計フェーズで確定）経過後にファイルへフラッシュする（see SL-9, CS-9）。
4. When Unity アプリケーションが正常終了（スタンドアロンの OnApplicationQuit、PlayMode 停止等）を迎えたとき、the Stage-Lighting-Volume Tab shall 保留中の未フラッシュ変更をファイルへ書き出す（see SL-9, D-9）。
5. When 本タブが起動し、`core-ipc-foundation` の IPC 接続が確立したとき、the Stage-Lighting-Volume Tab shall 保存ファイルを読み込み、アクティブプリセットの内容を **通常の state / event コマンド経路**（Requirement 3, 4, 5, 6）で送信して復元する（see CS-10）。
6. If 保存ファイルが存在しない（初回起動）場合, the Stage-Lighting-Volume Tab shall 復元処理をスキップし、メイン出力側の現在状態をそのまま UI に反映する。
7. If 保存ファイルの読み込みまたはパースに失敗した場合, the Stage-Lighting-Volume Tab shall エラーを診断ログに記録し、破損ファイルをバックアップ（リネーム等）したうえで初回起動扱いにフォールバックする（see CS-11 と同一思想）。
8. If 保存されたステージの Addressables key がステージ候補一覧に存在しなかった場合, the Stage-Lighting-Volume Tab shall ステージ選択を未選択状態に戻し、UI 上に「ステージ解決不能」警告を表示する（see SL-11）。Light / Volume は保存内容に従って復元する。
9. If 保存ファイル書き込みに失敗した場合（ディスク容量不足、権限エラー等）, the Stage-Lighting-Volume Tab shall エラーを診断ログに記録し、UI 上に保存失敗通知を提示したうえで次回変更時に再試行する（UI クラッシュ・描画停止を発生させない）。
10. The Stage-Lighting-Volume Tab shall 保存ファイルの配置・フォーマットを設計フェーズで確定することを要件として明記し、利用者プロジェクトでの配置場所（Application.persistentDataPath 配下等）が差し替え可能な構造を維持する。
11. When 復元 state / event コマンド送信中に一部の Light 追加や Volume Override の送信が失敗した場合, the Stage-Lighting-Volume Tab shall 失敗箇所のみをエラー表示で UI に反映し、他の復元処理は継続する（see Requirement 7 第 6 項）。

---

### Requirement 9: 失敗・縮退ハンドリング

**Objective:** 配信オペレーターとして、ステージアセットが壊れていた・範囲外の値が入った・メイン出力側がエラーを返した等の異常時でも、タブや UI シェル全体がクラッシュせず、問題箇所だけが縮退表示される状態を得たい。そうすれば配信中の部分的な障害が全体停止に発展せず、運用継続性を確保できる。

**Note:** 本要件は SL-11, SL-12 の方針を具現化し、`output-renderer-shell` Requirement 5（メイン出力描画にエラー UI を出さない）・`ui-toolkit-shell` Requirement 9（フェイルセーフ）と整合する。

#### Acceptance Criteria

1. If ステージ Addressables key に対応するアセットが解決不能であった場合, the Stage-Lighting-Volume Tab shall 該当プリセットのステージ選択を未選択状態に戻し、UI 上に警告バッジを表示する（see SL-11, Requirement 8 第 8 項）。
2. If メイン出力側からステージロード失敗イベントが通知された場合, the Stage-Lighting-Volume Tab shall UI 上に切替失敗を提示し、直前のステージ状態を維持する（see SL-11, Requirement 3 第 8 項）。
3. If Light プロパティまたは Volume Override param に範囲外の値が入力された場合, the Stage-Lighting-Volume Tab shall 送信を抑止し、UI 上でバリデーションエラーを該当コントロール近傍に表示する（see SL-12, Requirement 5 第 7 項, Requirement 6 第 7 項）。
4. If メイン出力側から Light / Volume / Stage 関連のエラー event が通知された場合, the Stage-Lighting-Volume Tab shall 該当 UI 要素のみをエラー状態に切り替え、他の UI 要素（他 Light、他 Override、プレビュー等）の表示・操作は継続する。
5. If Command 送信 API が接続未確立エラーまたはサイズ上限超過エラー（D-11）を返した場合, the Stage-Lighting-Volume Tab shall エラーを UI 側診断領域に記録し、UI クラッシュ・描画停止を発生させない（see UI-5）。
6. If `ui-toolkit-shell` の非同期ロードが失敗した場合（ステージサムネイル等）, the Stage-Lighting-Volume Tab shall 当該 UI 要素のみをデフォルト画像やエラー表示に切り替え、タブ全体のレンダリング・他の UI 要素の応答性を維持する。
7. The Stage-Lighting-Volume Tab shall いかなる失敗経路においても、メイン出力（Display 2+）へ警告・エラー UI を描画しない（see OR Requirement 5、docs/requirements.md §6.2）。
8. While メイン出力側との IPC 接続が切断している間, the Stage-Lighting-Volume Tab shall Stage / Light / Volume の操作 UI を安全に非活性化または保留状態に切り替え、接続回復後に復帰可能な状態を維持する（see ui-toolkit-shell Requirement 9）。
9. When IPC 接続が回復したとき、the Stage-Lighting-Volume Tab shall ステージ候補一覧・Light 一覧・Volume Override メタデータおよび現在値を再取得し、UI を現時点のメイン出力側状態に同期する。
10. While メイン出力側が Display 1 フォールバック描画中である場合（OR-1 の継承相当）、the Stage-Lighting-Volume Tab shall `ui-toolkit-shell` の UI 通知経路（UI Requirement 9 第 6 項）を介して誤配信リスクを示す警告を表示する（具体 UX は設計フェーズで確定）。

---

### Requirement 10: 観測性・診断可能性

**Objective:** 開発者・配信運用者として、本タブで発生する不具合が「UI 起因」「引きカメラ描画起因」「IPC 送受信起因」「Addressables 起因」「永続化 I/O 起因」のいずれかを即座に切り分けたい。そうすれば問題切り分けに要する時間を最小化し、本番配信中でも迅速に対応できる。

**Note:** 本要件の診断出力は `output-renderer-shell` Requirement 5 および `ui-toolkit-shell` Requirement 11 第 7 項に従い、UI 側（Display 1）またはコンソールへのみ流し、メイン出力（Display 2+）に一切描画しない。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 本タブの初期化・UIDocument アタッチ完了・引きカメラ起動・ステージ候補取得・Volume Override メタデータ取得・Light 一覧購読登録・永続化読込の各段階の開始・完了・失敗をログ出力する。
2. The Stage-Lighting-Volume Tab shall ステージ切替（event 送信）、Light 追加・削除（event 送信）、Light プロパティ変更（state 送信）、Volume Override enabled / param 変更（state 送信）、プリセット操作の対象識別子・topic・送信時刻をログレベルに応じて出力する。
3. When メイン出力側から Light 追加完了イベント（lightId 付き）・ステージロード完了／失敗イベント・エラー event を受信したとき、the Stage-Lighting-Volume Tab shall 対象識別子・イベント種別・受信時刻を診断ログに記録する。
4. When 非同期ロード（Addressables 経由のステージサムネイル等）の失敗イベントを受信したとき、the Stage-Lighting-Volume Tab shall 対象 Addressables key・失敗事由を診断ログに記録する。
5. When 永続化ファイルの読込・書込に失敗したとき、the Stage-Lighting-Volume Tab shall ファイル識別子・失敗事由を診断ログに記録する（see Requirement 8）。
6. The Stage-Lighting-Volume Tab shall 診断ログを Unity コンソールまたは UI 側診断領域（`ui-toolkit-shell` が提供するもの）にのみ流し、メイン出力サーフェスへ一切描画しない（see docs/requirements.md §6.2）。
7. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the Stage-Lighting-Volume Tab shall ログレベルを外部から切替可能にする（`ui-toolkit-shell` Requirement 11 第 8 項と整合）。
8. The Stage-Lighting-Volume Tab shall 診断に必要な最小限の状態（現在のアクティブプリセット名、現在のステージ ID、Light 数、エラー状態の Light 数、有効 Override 数、進行中の非同期ロード件数、永続化最終保存時刻、IPC 接続状態）を外部から取得可能な形で公開する。

---

### Requirement 11: スタンドアロンビルドと Unity Editor PlayMode の両対応

**Objective:** 配信運用者および開発者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で、本タブの挙動（UI 表示、引きカメラ、Stage/Light/Volume 操作、永続化）が同一であることを得たい。そうすれば開発中の検証と本番運用の挙動差を最小化でき、Editor PlayMode で配信前リハーサルが完結する。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。ドメインリロードに跨る状態維持は試みない。永続化ファイルは Edit モードからも読めるファイルとして存在するが、本タブの処理ロジック自体は PlayMode 内で動作する。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動し、`ui-toolkit-shell` のプリロードが完了したとき、the Stage-Lighting-Volume Tab shall 本タブの UI 初期化・引きカメラ起動・ステージ候補取得・Light 一覧購読・Volume メタデータ取得・永続化復元を自動的に実施する。
2. When Unity Editor が PlayMode に入ったとき、the Stage-Lighting-Volume Tab shall スタンドアロン時と同一手順で UI 初期化・引きカメラ起動・購読・復元を実施する（see D-9）。
3. When Unity Editor が PlayMode を終了したとき、the Stage-Lighting-Volume Tab shall 保留中の未フラッシュ永続化データを書き出し、購読を解除し、引きカメラ・RenderTexture・内部状態をクリーンアップして Edit モードに残留物を残さない（see D-9, Requirement 8 第 4 項）。
4. While PlayMode の開始と停止が繰り返される間, the Stage-Lighting-Volume Tab shall 購読重複・UI 要素重複生成・RenderTexture 残留・永続化ファイルのロック残存を発生させず、毎回クリーンに再初期化する。
5. The Stage-Lighting-Volume Tab shall Unity Editor の **Edit モード** では本タブの実行時ロジック（UI 初期化、引きカメラ起動、IPC 購読、永続化読込等）を起動しない（see D-9）。
6. The Stage-Lighting-Volume Tab shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに永続化ファイルから復元する（see D-9, CS-10）。
7. The Stage-Lighting-Volume Tab shall スタンドアロン時と Editor PlayMode 時で、オペレーターから見た UI 挙動・引きカメラ操作性・Stage/Light/Volume 操作レイテンシ特性・永続化挙動を同一に保つ。

---

### Requirement 12: 本 spec 単体での検証可能性

**Objective:** spec オーナーとして、本タブを `output-renderer-shell` 側の Light / Stage / Volume アダプタ実装や実ステージアセットがそろう前に検証したい。そうすれば Wave 3 の 3 タブを並行開発する際に、モックを介して本タブの UI と IPC 契約を独立に検証できる。

**Note:** 本要件は `ui-toolkit-shell` Requirement 10（単体検証）と `core-ipc-foundation` Requirement 8（自己ループ）を活用する。メイン出力側のステージ／Light／Volume ハンドラをモックアウトするため、IPC 契約上の応答を返すテストダブルを用意することで、本タブの全挙動を実行可能にする。

#### Acceptance Criteria

1. The Stage-Lighting-Volume Tab shall 実ステージアセット・実メイン出力側の Light / Volume 実装が接続されていなくても、IPC 契約上のモック応答（ステージ候補一覧、Light 一覧、Light 追加完了 + lightId、Volume Override メタデータ、各 state 反映応答、エラー event）を与えるテストダブルと連携して、UI の全表示・操作経路を実行できる構造とする。
2. The Stage-Lighting-Volume Tab shall `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）と `ui-toolkit-shell` のモック受容構造（UI Requirement 10 第 6 項）を利用して、Command 送信 API 経由の state / event / request を自プロセス内で送受信するテストケースを提供する。
3. The Stage-Lighting-Volume Tab shall 永続化 I/O 部分について、ファイルシステムに依存しない差し替え可能なストレージ抽象（メモリ上ダブル等）を受け入れる構造を備える。
4. The Stage-Lighting-Volume Tab shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物、モックステージ候補・モック Light 一覧・モック Volume メタデータを含む）を提供する。
5. When 本タブ単体のテスト実行が行われたとき、the Stage-Lighting-Volume Tab shall 次の挙動を検証するテストケースを提供する：ステージ候補 UI の反映、ステージ切替 event 送信、Light 追加 event 送信と lightId 付き完了イベント反映、Light プロパティ state 送信、Light 削除 event 送信、Volume Override enabled 切替、Volume param state 送信、プリセット CRUD、プリセット切替時の一括 state/event 送信、不可用ステージ復元時の未選択 + 警告挙動、IPC 切断中のフェイルセーフ挙動、永続化の書込・読込・破損フォールバック、引きカメラの非アクティブ時プレビュー停止。
6. The Stage-Lighting-Volume Tab shall テスト時に Addressables の代わりに差し替え可能なアセット解決モック（ステージ候補・サムネイル・存在判定）を受け入れる構造を備える。
7. The Stage-Lighting-Volume Tab shall テスト時に SceneViewStyleCameraController の代わりに差し替え可能なプレビューカメラ抽象（ダミー RenderTexture 描画等）を受け入れる構造を備える。
8. The Stage-Lighting-Volume Tab shall テスト時に時刻（デバウンスタイマー、タイムアウト等）を制御可能にするための時刻抽象を受け入れる構造を備える（設計フェーズで具体 API を確定）。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選、上流 spec 決定の積極的継承）
- **本 spec 固有の決定**: 12 件（SL-1〜SL-12）
- **継承**:
  - `core-ipc-foundation` の D-1（単一 Unity アプリ + LocalHost）、D-3（メインスレッド配信）、D-4（UI クライアント）、D-5（JSON / WebSocket）、D-7（coalesce / FIFO）、D-9（PlayMode のみ常駐）、D-10（PublishState / PublishEvent）、D-11（1 MB 上限）
  - `output-renderer-shell` の OR-1（Display 2 フォールバック時の UI 警告）、OR-2（state 競合 last-write-wins）
  - `ui-toolkit-shell` の UI-1（起動時プリロード）、UI-2（表示/非表示切替）、UI-3（USS 差し替え）、UI-4（共通 UI コンポーネントライブラリ）、UI-5（Command 送信 API）、UI-6（Addressables）、UI-7（タブ共通 UI 状態永続化なし）
  - `character-selection-tab` の CS-12（名前付きプリセット CRUD + activate）を本タブのプリセット設計に横展開
- **主要な発見（本 spec 固有）**:
  - 引きカメラは **タブ UI 内の RenderTexture プレビュー** として実装する（SL-1）。配信に載るメイン出力カメラとは完全に別のカメラであることを要件レベルで明文化し、spec #6（camera-switcher-tab）との責務境界を明確化。
  - Light GameObject はメイン出力側が所有（SL-2）、lightId はメイン出力側が採番（SL-3）。docs/requirements.md §3.2 の「UI から直接メイン出力側オブジェクトを触らない」契約と、CS-1 と同一方針を維持。
  - トピック粒度はプロパティ単位で細分化（SL-6）。`light/{lightId}/intensity` や `volume/override/bloom/intensity` のように、coalesce が別プロパティの中間値を巻き込まないよう設計。
  - Volume Override はメタデータ駆動で動的 UI 生成（SL-7）。利用者プロジェクトの独自 VolumeComponent も同じ経路で露出可能。CS-5 のアバター個別設定動的 UI と同一思想。
  - プリセットは Stage + Lights + Volume を 1 単位（SL-8）。切替時は通常 state/event 経路で一括送信し、メイン出力側の特殊ハンドラ不要（CS-10 / CS-12 準拠）。
  - 引きカメラ視点は永続化対象外（SL-10）。永続化は Stage ID + Light 構成 + Volume 構成のみに限定（SL-8, Requirement 8）。
- **残留リスク（設計フェーズで継続検討）**:
  - R-1: トピック命名規約の具体値（`light/{lightId}/intensity` 等の prefix / ID フォーマット）は設計フェーズで確定。
  - R-2: Volume Override メタデータ API（SL-7）の具体スキーマ。URP の VolumeComponent / VolumeParameter 公開メタデータの棚卸し（カスタム Override 対応の要否、パラメータの型表現、override state flag 相当の扱い）。
  - R-3: Light 追加・削除・プロパティ変更のタイムアウト値（Requirement 4 第 7 項等）の具体値。D-8 の 5 秒を踏襲するか本タブ固有で短縮するか。
  - R-4: プリセット切替の適用セマンティクス（一括 remove → add か差分適用か）の具体実装方針（Requirement 7 第 7 項、SL-8）。切替中の中間状態の視覚的影響を最小化する戦略。
  - R-5: プリセット切替中の部分失敗時のロールバック方針（Requirement 7 第 6 項）。本 spec は「部分適用のまま継続 + 警告」を暫定採用したが、配信事故防止観点での再評価は設計フェーズで実施。
  - R-6: 永続化ファイルのフォーマット（JSON / バイナリ）、配置（Application.persistentDataPath 配下の相対パス）、バージョニング方針、破損時のバックアップ命名規約。
  - R-7: デバウンス時間（Requirement 8 第 3 項）の具体値。CS-9 の想定値（例: 500ms）を踏襲するか、Light プロパティの高頻度変更に合わせて再検討。
  - R-8: 操作中コントロールへの state 逆流時の競合解消（Requirement 5 第 8 項、Requirement 6 第 8 項）。オペレーターが操作中の項目については逆流 state を一時抑止する等の UX 方針を確定。
  - R-9: 引きカメラ（プレビュー用）がメイン出力シーンをレンダリングする場合の具体構成（共有シーン／別シーン／レイヤー分離）と、メイン出力描画への GPU 負荷評価（Requirement 2 第 10, 11 項）。
  - R-10: Light Type 変更時の派生プロパティ（Spot Angle / Range 等）の初期値扱い（Requirement 5 第 4 項）。
  - R-11: UI 上の警告バッジ・エラー状態・接続断通知・切替失敗の具体 UX（配色、アイコン、テキスト）は `ui-toolkit-shell` のスキン差し替え方針（UI-3）との整合性を維持しつつ設計フェーズで確定。
  - R-12: ステージ候補一覧のサムネイル提供経路（Addressables 経由、サムネイルキー導出規約、サイズ規約）は CS-13 と整合させつつ設計フェーズで確定。
  - R-13: 引きカメラが映すシーンに「配信に載るキャラクター（RAC が扱う Slot）」を含めるかどうかの UX。含めると画作り時にキャラクター位置を確認できるが、キャラクター選択タブとの責務境界を設計フェーズで整理する必要あり。
  - R-14: SceneViewStyleCameraController のライセンス・バージョン固定方針（docs/requirements.md §6.4 と整合）。

## Dig Summary（本 spec 固有の追加分）

- **本 spec 固有の追加決定**: SL-13（プレビュー内容はステージ + ライティング + キャラすべて）、SL-14（Light 数の上限なし、警告なし）
- **継承**: core-ipc-foundation の D-1〜D-11、output-renderer-shell の OR-1, OR-2、ui-toolkit-shell の UI-1〜UI-7、character-selection-tab の CS-12（プリセット設計パターン）
- **残留リスク追加**:
  - R-SL-13-1: プレビューカメラとメイン出力カメラの同時描画による GPU 負荷が本番配信の fps を脅かさないよう、プレビューの解像度・描画頻度の上限を設計フェーズで決定
  - R-SL-14-1: 多数 Light 生成時のパフォーマンス劣化はオペレーター責務だが、運用監視のため診断 API で現在 Light 数を露出する。将来のパフォーマンス監視 spec で fps 連動警告を検討
