# Requirements Document

## Project Description (Input)
camera-switcher-tab

本番配信中のカメラスイッチャー UI。どのカメラの映像をメイン出力に流すかを切り替える。差し替え前提の最小機能で、SceneViewStyleCameraController + UniversalCamerawork + OSC 送信 + 適用までの一連パイプラインを提供する。

スコープ（現フェーズ）:
- SceneViewStyleCameraController で直感的にカメラを操作する（入力）
- 操作中のカメラ状態を UniversalCamerawork（UCAPI Flat Record）でシリアライズする（データ化）
- シリアライズしたカメラデータを OSC で LocalHost にリアルタイム送信する（伝送）
- LocalHost で受信したカメラデータを、各メイン出力ディスプレイの Camera コンポーネントに適用する（適用）
- 簡易的なスイッチャー UI（現在アクティブなカメラの選択・切替）
- 各カメラに紐づく URP Local Volume（カメラ Volume）の編集 UI
- カメラ切替と連動した Volume 切替

非目標（次フェーズへ）:
- トランジション（ディゾルブ、補間）
- PVW/PGM のマルチカメラ同時管理
- 外部ハードウェアスイッチャー連携
- タイムライン録画・リプレイ
- 他タブ（キャラクター選択、ステージ）の機能
- UI Toolkit シェル基盤そのもの（spec #3 に分離）

対応要件: docs/requirements.md の §4.1, §5.3
上位計画: docs/spec-breakdown.md の spec #6（Wave 3、output-renderer-shell と ui-toolkit-shell に依存）

上流決定の継承:
- D-1: 単一 Unity アプリ + LocalHost ループバック
- D-3: 受信コールバックは Unity メインスレッド
- D-4: UI 側クライアント / メイン出力側サーバ
- D-5: WebSocket / JSON（UI 設定操作用）
- D-10: PublishState / PublishEvent の使い分け
- UI-6: 追加アセットは Addressables 経由
- CS-12 / SL-8: 名前付きプリセット複数保持のパターン
- SL-6: トピック粒度のパターン

採用パッケージ:
- UniversalCamerawork (UCAPI): https://github.com/Hidano-Dev/UniversalCamerawork
  - カメラ状態の共通フォーマット（C++ DLL + UCAPI4Unity UPM）
  - Flat Record 128 byte + Header 10 byte、CRC16-CCITT 整合性検証
  - MessagePack シリアライズ対応
- SceneViewStyleCameraController: https://github.com/Hidano-Dev/SceneViewStyleCameraController

重要アーキテクチャ論点:
- カメラデータの伝送には **OSC**（Open Sound Control）を使用する。これは core-ipc-foundation の WebSocket/JSON 経路とは別チャネルである。
- UI 側（タブ）でカメラ操作 → UCAPI でデータ化 → OSC で localhost 送信 → メイン出力側で受信し各ディスプレイの Camera に適用、という 4 ステップのパイプライン。
- カメラスイッチャーは将来より高性能なものに差し替え前提（docs/requirements.md §5.3.4）。本フェーズは「差し替え可能な最小機能」。

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec（`core-ipc-foundation`, `output-renderer-shell`, `ui-toolkit-shell`, `character-selection-tab`, `stage-lighting-volume-tab`）の決定（D-1〜D-11, OR-1, OR-2, UI-1〜UI-7, CS-1〜CS-13, SL-1〜SL-14）は暗黙に継承される。本 spec 固有の暗黙デフォルトは以下の通り。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| CSW-1 | カメラデータ伝送の二系統構成 | **カメラの transform 連続値（position / rotation / focal length / aspect / タイムコード等）は OSC チャネルでメイン出力側へ送る。カメラの生成・削除・アクティブ切替・Local Volume 編集・メタデータ（名前・初期値等）・プリセットは `core-ipc-foundation`（WebSocket/JSON）を用いる**。OSC は `core-ipc-foundation` の PublishState 経路には **載せない**（別チャネル扱い、D-5 の対象外）。 | docs/requirements.md §5.3.3 に基づき、カメラ状態の共通フォーマットは UCAPI Flat Record（MessagePack 対応）で、伝送チャネルは OSC と決まっている。一方で UI 操作の state/event は D-5 の JSON/WebSocket 基盤に揃えた方が実装一貫性が保たれる。60 Hz 以上出得る transform ストリームを WebSocket/JSON 経路（D-7 の coalesce 前提）に載せるのは設計目的が噛み合わない。二系統に分離する方が論理的にも整合する。 | 中（2 本のチャネルを同時管理する実装負荷。接続断の挙動・時刻同期・起動順序の整理が必要） |
| CSW-2 | カメラスイッチャー差し替え前提の契約境界 | **本 spec が定義する「カメラデータ OSC 伝送」「カメラ生成・削除・アクティブ切替の IPC 契約」「Local Volume 編集の IPC 契約」「プリセット CRUD の IPC 契約」は、差し替え前提の最小 API 契約として切り出す**。将来のより高性能なカメラスイッチャー（トランジション／PVW-PGM／タイムライン連携等）は、同じ契約（あるいは後方互換に拡張した契約）を満たす別実装として差し替え可能とする。 | docs/requirements.md §5.3.4 および §6.3「カメラスイッチャーは差し替え前提の構造であること」の直接反映。本フェーズで定義する API 契約が、将来の差し替え時に破壊的変更とならないよう最小限に留めることを要件レベルで明示する。 | 中（契約を過剰にシンプルにすると、将来の高機能スイッチャーで再設計が必要になる可能性。最小 API の範囲を設計フェーズで精査する） |
| CSW-3 | カメラ操作 UI の実装形態 | **本タブの UI 上で SceneViewStyleCameraController を用いてカメラを操作する**。UI 内のプレビューパネル（RenderTexture）上でマウス回転・パン・ズームを受け付け、操作対象は「本タブ内のアクティブ編集対象カメラ」（アクティブ放送カメラとは独立に編集セッションを切替可能）とする。プレビュー UI 構成は CSW-16 に従い **マルチプレビュー（全カメラの縮小サムネイル）+ 大きなアクティブカメラプレビュー** の放送局スイッチャー盤スタイルとする。 | docs/requirements.md §5.3.3 第 1 項（入力）と §5.3.5 の UX 論点を反映。カメラ切替 UI と編集対象カメラの一致・不一致は設計フェーズで UX 判断するが、本要件段階では「オペレーターが直感的に動かせる RenderTexture プレビュー経由の操作」を構造的に担保する。 | 中（UI 内プレビュー描画のコストがメイン出力のフレームに干渉しないよう設計フェーズで最適化方針を決定する必要あり。SL-10 の「プレビュー描画は Display 1 内に閉じる」方針と整合させる） |
| CSW-4 | カメラの state/event の役割分担 | **カメラの transform 連続値は OSC で送る（WebSocket 経路の PublishState には載せない）**。**カメラの create / delete / active-set は `PublishEvent`（FIFO 必須）で送る**。**カメラメタデータ（名前・カメラタイプ・初期 transform デフォルト値等）は `PublishState`（coalesce 対象）で送る**。**Local Volume の Override パラメータは `PublishState`、Local Volume の有効/無効・Override 追加削除は `PublishEvent`** とする。 | D-10 と CS-6 / SL-5 の直接反映。transform の連続値は OSC（別チャネル）に振り分けるため、WebSocket 経路の PublishState は使用しない。create/delete/active-set のような離散で漏らしてはならない操作は FIFO、設定値や名前等の冪等な値は coalesce 対象とする分類を要件レベルで固定する。 | 低 |
| CSW-5 | カメラ識別子の採番 | **カメラ識別子（cameraId）はメイン出力側が採番し、`PublishEvent` によるカメラ生成完了通知に含めて UI 側へ返す**。UI 側は「生成要求を送る → cameraId 付き完了イベントを受信 → 以降の state/OSC 送信に cameraId を使用」というフローを採用する（SL-3 と同一方針）。 | UI 側と他クライアント間で採番衝突を避けるため、採番権限をメイン出力側（権威ある状態所有者、D-4）に集中させる。OSC のアドレスパターンにも cameraId を含めることで、1 本の OSC チャネルで複数カメラの transform ストリームを区別できる。 | 中（生成要求と cameraId 発行の間に短い待ち時間が発生する。UI 上は「生成中」表示で吸収する。OSC 伝送の開始タイミングを cameraId 確定後にそろえる設計が必要） |
| CSW-6 | OSC アドレスパターンの設計方針 | **OSC アドレスは cameraId を含む階層構造（例: `/ucapi/camera/{cameraId}/flat`）を採用する**。1 メッセージに UCAPI Flat Record（128 byte）+ Header（10 byte）相当の blob を 1 枚載せる。具体パス・引数型・マルチキャスト可否は設計フェーズで確定する。Flat Record の整合性検証は CRC16-CCITT（UCAPI 規約）に従う。 | docs/requirements.md §10「OSC によるカメラデータ送信のアドレスパターン・メッセージ構造の詳細」の未確定項目を要件段階で方向付ける。cameraId 階層化により、受信側で単一リスナーから複数カメラへのディスパッチが自然に行える。将来のマルチカメラ多重ストリーム化にも拡張可能。 | 中（具体アドレスは設計フェーズで確定。UCAPI 側のアドレス規約と衝突しないよう、UCAPI のベースアドレスを継承する） |
| CSW-7 | OSC 送信側・受信側のロール配置 | **UI 側（本タブ）を OSC 送信側（クライアント）、メイン出力側を OSC 受信側（サーバ）とする**。UI 側が編集中のカメラ transform を連続送信し、メイン出力側はそれを購読して各 Camera コンポーネントに適用する。OSC のポート・ホストは設定ファイルから読み込み、未指定時はデフォルトを使用する（D-6 と同思想）。 | D-4（UI クライアント / メイン出力サーバ）と対称にする。IPC（WebSocket）と OSC のロール方向を揃えることで、接続管理とライフサイクル（起動順序、切断対応）のメンタルモデルが 1 本化される。 | 低 |
| CSW-8 | OSC 受信コールバックのスレッド | **OSC 受信コールバックも Unity メインスレッドへディスパッチしたうえで Camera コンポーネントに適用する**（D-3 と同思想）。I/O はワーカースレッドで受け、SynchronizationContext 経由で Unity メインスレッドに渡す。 | 各 Camera の transform / focalLength 等のプロパティ設定は Unity API であり、ワーカースレッドから直接触れない。D-3 と同契約にすることで、メイン出力側の実装者が `core-ipc-foundation` 経由の受信と OSC 受信で同じスレッドモデルを扱える。 | 低（1 フレーム（〜16ms）遅延は配信要件上許容、D-3 と同じ論理） |
| CSW-9 | OSC メッセージの頻度上限と間引き方針 | **OSC 送信はメイン出力の描画フレームに同期する**（Unity の Update または LateUpdate で毎フレーム 1 回送信。60 fps 動作時は実質 60 Hz 相当）。UI 操作中に同一フレーム内で複数の transform 変化が発生しても、送信は **1 フレーム 1 メッセージ** に集約され、送信された値は「そのフレームの最終値」となる（UI 側で自然に coalesce 同等処理）。受信側は各フレームで到着した最新値を採用する（OSC には WebSocket 側の coalesce 機構は存在しないため、受信ハンドラ側で最新値優先処理を行う）。 | 配信の典型フレームレート（60 fps）を超える頻度は描画のレイテンシに寄与せず、帯域の無駄になる。描画フレーム同期にすることで、メイン出力の描画と transform 更新が自然にフェーズロックし、カメラ動作の滑らかさが保証される。30 Hz 等の低レート固定は、メイン出力 fps とズレるとカメラの動きがカクつく可能性があるため不採用。 | 中（低 fps 環境でメイン出力 fps が落ちた場合、OSC 送信レートも連動して下がる。UI 側のプレビューも同一シーンを描画するため、プレビューの描画フレームと OSC 送信を分離するか連動させるかを設計フェーズで確定する） |
| CSW-10 | カメラ作成時の既定パラメータ | **カメラ作成時の初期 transform・光学パラメータの既定値はメイン出力側が定義する**（例: 現在アクティブな引きカメラの視点を初期値とする等、具体方針は設計フェーズで確定）。UI 側は作成要求に「カメラタイプ（Perspective / Orthographic 等）」と任意の「カメラ名」を添えて送るのみで、数値は付けない（冪等に繰り返しても安定動作するため）。 | UI 側で初期値を決めると、将来のカメラタイプ追加時に UI 側の実装追従が必要になる。メイン出力側（権威）で決めれば、種別追加時に UI の改修が不要。 | 低 |
| CSW-11 | カメラ Local Volume の編集モデル | **Local Volume は Camera ごとに 1 つ割り当てられるものとして扱い、Override リストと各 Override の enabled/param の編集 UI を提供する**（SL-7 の「Global Volume」を「Local Volume」に置き換えた形）。Override 種別（Bloom, Tonemapping, ColorAdjustments 等）のメタデータ取得は IPC Request で行い、UI は動的生成する（CS-5 / SL-7 と同一方針）。Local Volume 自体の有効/無効も state として扱う。 | docs/requirements.md §5.3.5 の「各カメラに紐づく URP Volume（Local Volume）を UI から編集」を SL-7 と同じメタデータ駆動の枠組みで実装することで、UI 実装コストの重複を排除し、オペレーターの学習体験を統一する。 | 中（Local Volume の Unity 実装 / URP 仕様の確認が設計フェーズで必要。Global Volume との差異は「適用スコープ」のみであることを想定） |
| CSW-12 | カメラ切替と Local Volume の連動 | **カメラの active-set（アクティブ切替）イベントを受けて、メイン出力側で自動的に対応する Local Volume もアクティブに差し替える**。UI 側からは「カメラを切り替える」操作だけを送信すれば、Volume の有効化は付随的に行われる（UI 側は Volume の enable/disable を別途送らない）。 | docs/requirements.md §5.3.5「カメラ切替時に、対応する Volume の効果が自動的に切り替わること」の直接反映。カメラと Volume を 1:1 で束ね、UI 側がそれぞれを別々に同期させる必要を排除する。 | 中（Volume アクティブ切替がメイン出力側で確実に行われるよう、メイン出力側の契約として明示する必要がある。Volume の手動制御が欲しい高度ケースは本フェーズの非目標として整理する） |
| CSW-13 | プリセット設計 | **「カメラリスト（cameraId + カメラタイプ + カメラ名 + 初期 transform デフォルト）+ 各カメラの Local Volume 構成 + アクティブカメラ」を 1 単位とする名前付きプリセットを複数保持する（CS-12 / SL-8 準拠）**。本タブは `create / rename / duplicate / delete / activate` のプリセット CRUD UI とアクティブプリセット表示を提供する。プリセット切替時は通常の state/event コマンド経路（CSW-4）で一括適用し、メイン出力側では特殊ハンドラを用意しない（CS-12 / SL-8 と同パターン）。 | CS-12 / SL-8 と同構造。配信シーンごと（朝配信、コラボ配信、アクション配信等）のカメラレイアウト + Volume ルックを 1 ボタンで切り替え可能にする。他タブと UX を揃えることで、オペレーターの学習コストが下がる。 | 中（カメラの一括再構築では既存カメラの delete と新規 add を多数送ることになる。SL-8 と同様の整合性維持のための送信順序と中間状態の扱いを設計フェーズで確定する） |
| CSW-14 | プリセット永続化のタイミング | **プリセット内容の変更（カメラ追加削除、カメラメタデータ変更、Local Volume の state 変化、アクティブカメラの変更）があった都度、デバウンス（具体値は設計フェーズで確定）後にアクティブプリセットのファイルへフラッシュする**。アプリ終了時にも保留中をフラッシュする（CS-9 / SL-9 と同一方針）。カメラの transform 連続値（OSC で送られるストリーム）は永続化対象外であり、初期 transform デフォルト（プリセットの一部）のみ保存する。 | 配信現場では「保存忘れ」が配信事故に直結する。CS-9 / SL-9 と揃えることで UX 一貫性を得る。transform ストリームを毎フレーム保存するのは非現実的であり、初期デフォルトのみをプリセットに含めることで保存規模を抑える。 | 中（「初期 transform デフォルト」の更新タイミング＝オペレーターが意図的に保存したタイミングかどうかの UX 判断は設計フェーズで確定。現状案は「プリセット適用時の transform を初期値とし、以降の OSC 編集は永続化しない」方式） |
| CSW-15 | OSC 接続断時の挙動 | **OSC 送信側は接続断（UDP ではペアリング概念が弱いが、受信側ポートが開いていない等の初期化失敗）を検出したらログ出力し、UI 側は「OSC 送信不可」状態を診断 API から取得可能にする**。本タブの UI 操作・IPC（WebSocket）経由のカメラ CRUD 操作自体は OSC 断があっても継続できる構造とする（送信は単に届かず、メイン出力側のカメラ transform が更新されないだけで、UI シェルや他タブは動作を継続）。 | OSC は UDP 基盤で接続状態の検出が原理的に弱い。OSC 断を致命的障害扱いにするとフェイルセーフ契約（OR / UI-shell）と矛盾する。UI 側は最低限の診断露出に留め、メイン出力の描画を阻害しない。 | 中（UDP ベースでは「接続断」検出は初期化失敗時に限定される可能性が高い。設計フェーズで具体的な検出方法と診断情報の粒度を確定する） |
| CSW-16 | プレビュー UI の構成 | **マルチプレビュー + 大きなアクティブカメラプレビューの二層構成**。UI には全カメラの縮小サムネイル（小さな RenderTexture）を一覧として並べ、加えて「現在の編集対象 / アクティブカメラ」を大きな RenderTexture として強調表示する。放送局のスイッチャー盤 UX を模し、オペレーターが「どのカメラが何を映しているか」を一目で把握しながら切替・編集できるようにする。 | 差し替え前提の最小機能でありつつ、本フェーズでも「放送カメラのスイッチャー」として視認性と切替作業性を担保する。全カメラの状態を常に目視できることで、切替直前の確認作業を短縮できる。シングルプレビュー + リストだと「切替候補のカメラが今どう見えているか」を切替前に確認できず、本番作業向きでない。 | 中（N 台のカメラ全てに対し RenderTexture を割り当てて描画する分の GPU コスト増。サムネイルの解像度・更新頻度は設計フェーズで最適化する。CSW-9 の 60 Hz 同期と併せてフレーム予算を管理する） |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **カメラ・カメラ Volume 操作タブ（Camera Switcher Tab）** を定義する。Display 1 側の UI Toolkit シェル（spec #3 `ui-toolkit-shell`）内に配置される 3 タブのうち 1 つとして、本番配信中のカメラスイッチャー UI の責務を持つ。docs/requirements.md §5.3.4 の方針に従い、本フェーズは **「差し替え可能な最小機能」** として設計され、将来のより高機能なカメラスイッチャー（トランジション・PVW/PGM・タイムライン連携等）に差し替えられる契約境界を維持する（CSW-2）。具体的には：

1. **カメラ操作（入力）**：[SceneViewStyleCameraController](https://github.com/Hidano-Dev/SceneViewStyleCameraController) を用いたマウス回転・パン・ズーム操作を、本タブ UI 内の RenderTexture プレビューパネルで受け付け、オペレーターが直感的にカメラを動かせる（CSW-3）。
2. **UCAPI Flat Record へのシリアライズ（データ化）**：操作中のカメラ状態（position / rotation / focal length / aspect / タイムコード等）を [UniversalCamerawork](https://github.com/Hidano-Dev/UniversalCamerawork) (UCAPI) の Flat Record 形式（128 byte + Header 10 byte、CRC16-CCITT 整合性検証、MessagePack シリアライズ対応）に変換する。
3. **OSC による LocalHost 伝送（伝送）**：シリアライズしたカメラデータを OSC チャネルで LocalHost にリアルタイム送信する。これは `core-ipc-foundation` の WebSocket/JSON 経路とは **別チャネル** として扱う（CSW-1, CSW-7）。
4. **メイン出力側での適用（適用）**：メイン出力側が OSC を受信し、UCAPI Flat Record をデシリアライズして、各メイン出力ディスプレイの Camera コンポーネントに適用する（本 spec は OSC の契約とメイン出力側への期待を定義する。受信側実装そのものは `output-renderer-shell` の拡張またはアダプタ層の責務）。
5. **カメラスイッチャー UI**：現在アクティブなカメラを選択・切替できる **簡易スイッチャー UI**（ハードカット専用、ディゾルブ等のトランジションは本フェーズの非目標）を提供する（docs/requirements.md §5.3.4）。
6. **カメラごとの Local Volume 編集 UI**：各カメラに紐づく URP Local Volume（Bloom, DoF, Grain 等）を **メタデータ駆動の動的 UI** で編集する（SL-7 と同パターン、CSW-11）。
7. **カメラ切替と Local Volume の連動**：カメラのアクティブ切替（active-set）イベントを受けて、メイン出力側で自動的に対応する Local Volume も差し替わる（CSW-12）。
8. **IPC 契約に基づく状態同期（WebSocket チャネル）**：カメラの生成・削除・アクティブ切替は `PublishEvent`、カメラメタデータ（名前・タイプ等）と Local Volume の Override パラメータは `PublishState` で送信する（CSW-4）。カメラ transform の連続値は OSC 側に流すため、WebSocket 側の PublishState には **載せない**。
9. **名前付きプリセットの永続化と復元**：カメラリスト + 各カメラの初期 transform デフォルト + Local Volume 構成 + アクティブカメラを 1 単位とする名前付きプリセットを複数保持し、CRUD / アクティブ化 UI を提供する（CSW-13, CS-12 / SL-8 準拠）。
10. **失敗ハンドリング**：OSC 送信失敗・Local Volume メタデータ取得失敗・範囲外パラメータ・メイン出力側からのエラー event 等について、UI 側で安全に縮退し、メイン出力描画に波及させない（CSW-15）。

本 spec は **UI 側のタブ機能とその IPC/OSC 契約定義** に限定される。メイン出力シーンでの Camera GameObject 生成・破棄・プロパティ適用、OSC 受信側の実装、Local Volume コンポーネント操作、IPC トランスポートそのもの、UI Toolkit シェル基盤、メイン出力シーン骨格、他タブの機能、引きカメラ（`stage-lighting-volume-tab` のプレビュー）、カメラスイッチャーのトランジション・PVW/PGM・外部ハードウェア連携・タイムライン録画リプレイは本 spec の責務外である（CSW-2 および Project Description の非目標参照）。

## Boundary Context

- **In scope**:
  - 本タブの UIDocument（VisualTreeAsset + StyleSheet）と、`ui-toolkit-shell` の UXML/USS 配置規約（UI-3, UI-4）への適合
  - SceneViewStyleCameraController を用いた **編集対象カメラのプレビュー UI**（タブ UI 内の RenderTexture パネルへの埋め込み、CSW-3）
  - カメラ一覧 UI（追加・削除・選択・編集対象切替・アクティブ切替等の操作）とカメラごとのメタデータ編集（名前・タイプ等）
  - カメラの create / delete / active-set の `PublishEvent` 送信（CSW-4）
  - カメラメタデータ（名前・タイプ・初期 transform デフォルト）の `PublishState` 送信（CSW-4）
  - カメラ transform 連続値（position / rotation / focal length / aspect / タイムコード等）の **UCAPI Flat Record シリアライズ** と **OSC 送信**（CSW-1, CSW-6, CSW-7, CSW-9）
  - カメラ Local Volume の Override リスト UI と、Override ごとの `enabled` トグル・param 編集用動的 UI（メタデータ駆動、CSW-11）
  - Local Volume Override パラメータの `PublishState` 送信、Local Volume の enable/disable や Override 追加削除の `PublishEvent` 送信（CSW-4）
  - カメラ active-set と Local Volume アクティブ化の連動契約（CSW-12、UI 側は 1 操作を送れば足りる）
  - メイン出力側からの state / event / response の購読（カメラ一覧、Local Volume メタデータ、エラー event、cameraId 発行通知等、CSW-5）
  - 名前付きプリセット（カメラリスト + 各カメラ初期 transform デフォルト + Local Volume 構成 + アクティブカメラ）CRUD UI と切替操作（CSW-13）
  - プリセット内容の永続化（デバウンス即時保存、CSW-14）と起動時復元（通常 state/event 経路）
  - OSC 送信側クライアントのライフサイクル管理（起動・停止・ポート設定、CSW-7, CSW-15）
  - OSC 送信失敗・Local Volume メタデータ不整合・範囲外値・メイン出力エラー等の縮退挙動（CSW-15）
  - タブのアクティブ化／非アクティブ化時の購読登録／解除、および非アクティブ時のプレビュー描画一時停止（SL-10 と同パターン）
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 の継承）
  - 差し替え前提の最小 API 契約の維持（CSW-2、将来の高機能スイッチャーへの差し替え点を残す）
- **Out of scope**:
  - メイン出力シーンでの **Camera GameObject の実生成・破棄・プロパティ適用** 実装（メイン出力側の responsibility、CSW-5）
  - メイン出力シーンでの **OSC 受信・UCAPI Flat Record デシリアライズ・各 Camera コンポーネントへの適用** 実装（メイン出力側 / 受信アダプタ層の responsibility。本 spec は OSC 契約のみ定義）
  - メイン出力シーンでの **URP Local Volume コンポーネントへの値適用** 実装（メイン出力側の responsibility）
  - カメラスイッチャーの **トランジション・ディゾルブ・カット時の補間**（docs/requirements.md §5.3.4、本フェーズの非目標）
  - **PVW/PGM のマルチカメラ同時管理**（docs/requirements.md §5.3.4、次フェーズへ）
  - **外部ハードウェアスイッチャー連携**（docs/requirements.md §5.3.4、次フェーズへ）
  - **タイムライン録画・リプレイ**（docs/requirements.md §5.3.4、次フェーズへ）
  - UCAPI C++ DLL 本体の実装・修正（採用パッケージをそのまま使用）
  - SceneViewStyleCameraController 本体の実装・修正（採用パッケージをそのまま使用）
  - **引きカメラ（`stage-lighting-volume-tab` のプレビュー専用カメラ）の操作**（spec #5 の責務、SL-1）
  - `core-ipc-foundation` のトランスポート・シリアライゼーション・OSC トランスポート実装そのもの（OSC ライブラリの選定と実装は本 spec のスコープに含むが、WebSocket/JSON 基盤は spec #1 の責務）
  - `ui-toolkit-shell` のルート UIDocument・タブ切替機構・Command 送信 API 実装・非同期ロード基盤実装（spec #3 の責務）
  - `output-renderer-shell` のシーン初期化・ディスパッチャ・デフォルトカメラ配置（spec #2 の責務。本タブはそこにカメラを追加生成する側）
  - 他タブ（キャラクター選択、ステージ・ライティング・Volume）の機能
  - MoCap / アクター操作（キャラクター選択タブの責務）
  - ステージ・Global Volume・Light の操作（ステージ・ライティング・Volume タブの責務）
  - タブ共通 UI 状態（アクティブタブ、ウィンドウサイズ等）の永続化（UI-7 により永続化しない）
- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）の抽象インタフェースが利用可能で、`PublishState` / `PublishEvent` / `Request` / 受信購読が Unity メインスレッド配信で使えること（D-3, D-10）
  - `ui-toolkit-shell`（spec #3）が Command 送信 API・受信購読 API・共通 UI コンポーネントライブラリ・非同期ロード基盤・タブ配置規約を公開していること（UI-4, UI-5, UI-6）
  - `output-renderer-shell`（spec #2）が、本 spec で定義する state / event / request の topic を受け付けるハンドラを登録可能な構造であること。特にデフォルトカメラ配置（OR Requirement 1.2）・カメラルート GameObject の存在（OR Requirement 1.1）を前提とする
  - 採用パッケージ SceneViewStyleCameraController が Unity 6.3 で利用可能であり、カメラ制御コンポーネントを任意の Camera に取り付けて RenderTexture 出力を得られること
  - 採用パッケージ UniversalCamerawork（UCAPI / UCAPI4Unity UPM）が Unity 6.3 で利用可能であり、Flat Record（128 byte + Header 10 byte）のシリアライズ・CRC16-CCITT 検証・MessagePack ペイロード化が UPM API から実施できること
  - メイン出力側（`output-renderer-shell` の拡張またはアダプタ層）が、本 spec の IPC 契約 + OSC 契約に従って Camera / Local Volume を操作する実装を別途提供すること（本 spec はその相手方の存在を前提とした UI 側契約のみを定義する）
  - OSC ライブラリ（Unity 向け OSC 送受信ライブラリの選定は設計フェーズで確定、Addressables 経由ではなくアセンブリ同梱の想定）が Unity 6.3 で利用可能であること

---

### Requirement 1: カメラスイッチャータブ UIDocument の配置と UI Toolkit シェル統合

**Objective:** タブ spec の開発者として、本タブを `ui-toolkit-shell` の 3 タブのうち 1 枠に正しく載せ、起動時一括プリロード・表示/非表示切替のみのタブ遷移・メイン出力を 1 フレームもフリーズさせない要件を満たす形で統合したい。そうすればタブ独自のロード戦略を書かずに済み、シェル側の契約で性能要件を構造的に担保できる。

**Note:** 本要件は `ui-toolkit-shell` の Requirement 1（ルート UIDocument）・Requirement 2（タブ切替）・Requirement 3（起動時一括プリロード）の契約を受け入れる側の責務を定義する。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 本タブ専用の UIDocument（VisualTreeAsset）および StyleSheet を、`ui-toolkit-shell` が定義する UXML/USS 配置規約（UI-3 相当）に従って提供する。
2. The Camera Switcher Tab shall 本タブのルート要素および主要要素に対して、`ui-toolkit-shell` の USS セレクタ命名規約（クラス名プレフィクス等）を適用し、スキン差し替え経路（UI-3）から見た目を変更可能にする。
3. When `ui-toolkit-shell` が起動時プリロードを実行したとき、the Camera Switcher Tab shall 本タブの VisualTreeAsset・StyleSheet を同期的にアタッチ完了させ、タブ切替時に再 clone や再生成を発生させない（see UI-1, UI-2）。
4. When 本タブがアクティブ化されたとき、the Camera Switcher Tab shall USS の `display` / `visible` プロパティによる表示化のみで UI を提示し、VisualTreeAsset の再ロード・メインスレッドブロッキング処理を行わない（see UI-2）。
5. When 本タブが非アクティブ化されたとき、the Camera Switcher Tab shall `ui-toolkit-shell` が公開するタブ切替イベントに応じて、編集対象カメラのプレビュー描画を一時停止し、購読解除やストリーミング UI の停止など必要な状態保存処理を行う（see Requirement 2）。
6. The Camera Switcher Tab shall 本タブの UI 構築・表示切替処理において、メイン出力（Display 2+）の描画フレームに干渉する同期 I/O・メインスレッドブロッキング処理を一切含まない（see docs/requirements.md §4.2, §6.1）。
7. The Camera Switcher Tab shall 本タブのアセンブリ定義（asmdef）を独立させ、`ui-toolkit-shell` の公開 API、`core-ipc-foundation` の抽象インタフェース、採用パッケージ SceneViewStyleCameraController、採用パッケージ UniversalCamerawork（UCAPI4Unity）、および OSC ライブラリ（設計フェーズで選定）以外に直接依存しない参照方向を維持する。
8. Where 利用者プロジェクトが本タブの UXML を差し替える場合, the Camera Switcher Tab shall `ui-toolkit-shell` の UXML 差し替え拡張点（UI-3）を経由させ、必須要素の欠落があれば診断ログへ記録する。

---

### Requirement 2: 編集対象カメラのプレビュー UI と SceneViewStyleCameraController 連携（入力）

**Objective:** 配信オペレーターとして、本タブを開いている間、マウス回転・パン・ズームで編集対象カメラを直感的に動かせるプレビュー画面がタブ UI 内に表示され、配信に載るメイン出力カメラへの反映は OSC 経由でリアルタイムに行われる状態を得たい。そうすれば Unity Editor の Scene ビュー相当の操作感で、本番配信中でもカメラを自然に動かせる。

**Note:** 本要件は CSW-3 の決定に従い、編集対象カメラ操作は **タブ UI 内の RenderTexture プレビュー**（Display 1 側）として実装する。プレビューに映る内容や、編集対象カメラとメイン出力アクティブカメラの一致・不一致方針は設計フェーズで詳細化する（シングルプレビュー + 切替 / マルチプレビュー）。SceneViewStyleCameraController を採用し、Unity Editor の Scene ビュー相当の操作（マウス回転・パン・ズーム）を提供する（docs/requirements.md §5.3.3 第 1 項）。プレビューが Display 2+ 側に描画されないこと、および SL-10 の引きカメラプレビューとは独立した別カメラであることに注意。

#### Acceptance Criteria

1. The Camera Switcher Tab shall **SceneViewStyleCameraController を用いた編集対象カメラのプレビュー UI** を本タブ UI 内に用意し、その出力を **RenderTexture** に描画する（see CSW-3）。
2. The Camera Switcher Tab shall 当該 RenderTexture を本タブ UIDocument 内の **プレビューパネル**（UI Toolkit の `VisualElement` / `Image` 相当）に表示する。
3. The Camera Switcher Tab shall プレビュー RenderTexture を **Display 1（UI 側）にのみ描画** し、メイン出力サーフェス（Display 2+）には一切描画しない（see docs/requirements.md §6.2, OR Requirement 5）。
4. The Camera Switcher Tab shall 編集対象カメラの操作（マウス回転・パン・ズーム）を SceneViewStyleCameraController の標準 UX に従って提供し、オペレーターはプレビューパネル上でインタラクトできる。
5. When オペレーターがプレビュー上でカメラを操作したとき、the Camera Switcher Tab shall 編集対象カメラの transform / 光学パラメータの更新をフレーム単位で検出し、Requirement 4 の OSC 送信経路へ転送する（see CSW-1, CSW-9）。
6. The Camera Switcher Tab shall 編集対象カメラを **`stage-lighting-volume-tab` の引きカメラ（プレビュー）とは別のカメラ** として実装し、両者の視点操作が相互に影響しないことを構造的に保証する（see SL-1）。
7. When 本タブが非アクティブ化されたとき、the Camera Switcher Tab shall 編集対象カメラのプレビュー描画および OSC 連続送信を一時停止し、不要な GPU・ネットワークリソース消費を抑制する（see Requirement 1 Acceptance Criteria 5）。
8. When 本タブが再アクティブ化されたとき、the Camera Switcher Tab shall プレビュー描画を再開し、直前の編集対象カメラ選択と視点状態を維持する（永続化はしないが、PlayMode 内での状態は保持する）。
9. The Camera Switcher Tab shall 編集対象カメラの選択（どのカメラを今編集しているか）を UI 上で明示的に切替可能にし、切替時に OSC 送信先 cameraId も当該カメラへ切り替える（see Requirement 4, CSW-5）。
10. The Camera Switcher Tab shall プレビュー描画処理が **メイン出力（Display 2+）の描画フレームに干渉しない** ことを設計上保証する（レンダラ分離・低優先度スケジューリング等の具体手段は設計フェーズで確定）。
11. Where プレビュー RenderTexture がメイン出力シーンのオブジェクト（ステージ・Light・キャラクター等）をレンダリングする必要がある場合, the Camera Switcher Tab shall メイン出力シーンの共有利用方法（共有シーン／別シーン／レイヤー分離等）を設計フェーズで確定し、メイン出力カメラとの描画競合が生じない配置とする（see SL-1 と同思想）。

---

### Requirement 3: カメラ状態の UCAPI Flat Record シリアライズ（データ化）

**Objective:** システム開発者として、Unity Camera の状態（position / rotation / focal length / aspect / タイムコード等）を UCAPI の共通フォーマット（Flat Record 128 byte + Header 10 byte）へ確実にシリアライズしたい。そうすれば将来の外部ツール・多 PC 連携・別実装のカメラスイッチャーとも同じバイナリ形式でカメラ状態を相互運用できる。

**Note:** 本要件は docs/requirements.md §5.3.3 第 2 項（データ化）を反映する。UCAPI は C++ DLL + UCAPI4Unity UPM として提供され、Flat Record は 128 byte の POD 構造、Header は 10 byte、整合性検証は CRC16-CCITT、ペイロードは MessagePack シリアライズに対応する。本 spec は UCAPI4Unity の公開 API を利用するのみで、Flat Record 仕様そのものは修正しない。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 編集対象カメラの Unity Camera コンポーネント状態（position、rotation、focal length、aspect、near/far、field of view、タイムコード等、具体項目は UCAPI Flat Record 仕様に準拠）を UCAPI4Unity の公開 API を通じて **UCAPI Flat Record（128 byte）+ Header（10 byte）** 形式にシリアライズする。
2. The Camera Switcher Tab shall シリアライズに際して UCAPI 規約の **CRC16-CCITT 整合性検証** を付与する。
3. The Camera Switcher Tab shall シリアライズ結果を **UCAPI が提供する MessagePack 経路** または Flat Record raw バイナリとして OSC 送信経路へ引き渡す（具体形式は Requirement 4 で規定）。
4. The Camera Switcher Tab shall Unity Camera の座標系・光学パラメータの単位と UCAPI Flat Record の単位の差異（例: メートル vs. Unity units、度 vs. ラジアン等、具体差異は設計フェーズで UCAPI 仕様を棚卸し）を正しく変換する。
5. If Unity Camera から取得した値が UCAPI Flat Record の値域外（NaN、Inf、範囲外の focal length 等）であった場合, the Camera Switcher Tab shall 該当フレームのシリアライズを抑止し、診断ログに記録する（UI クラッシュ・描画停止を発生させない）。
6. The Camera Switcher Tab shall シリアライズ処理を Unity メインスレッド上で実施し、Unity API アクセス時のスレッドアクセス違反を回避する（see D-3）。
7. The Camera Switcher Tab shall UCAPI4Unity の API 変更に追従可能な参照分離構造（薄い薄いアダプタレイヤ）を備え、UCAPI バージョン更新時に本タブ本体のロジックへの影響を最小化する。
8. Where UCAPI4Unity が将来 Flat Record の拡張版（例: 256 byte 等）を提供する場合, the Camera Switcher Tab shall 拡張対応が **差し替え前提の最小 API 契約**（CSW-2）の改定で収まるよう、フォーマット依存をシリアライズ層に閉じ込める。

---

### Requirement 4: OSC による LocalHost 伝送（伝送）

**Objective:** システム運用者として、UI 側で編集中のカメラ transform が OSC 経由でメイン出力側に低遅延で届き、配信映像にリアルタイムで反映される状態を得たい。そうすればオペレーターの操作がそのまま本番映像に反映される自然な配信体験が得られる。

**Note:** 本要件は docs/requirements.md §5.3.3 第 3 項（伝送）を反映する。OSC は `core-ipc-foundation` の WebSocket/JSON とは **別チャネル** として扱う（CSW-1）。UI 側（本タブ）が送信クライアント、メイン出力側が受信サーバとなる（CSW-7、D-4 と対称）。アドレスパターン（例: `/ucapi/camera/{cameraId}/flat`）と具体ポートは設計フェーズで確定する（CSW-6）。

#### Acceptance Criteria

1. The Camera Switcher Tab shall OSC 送信クライアントを本タブ内で初期化し、接続先ホスト（デフォルト `127.0.0.1`）とポート（設計フェーズで確定）へ UCAPI Flat Record を送信する（see CSW-6, CSW-7）。
2. The Camera Switcher Tab shall OSC の接続先ホスト・ポート等の基本パラメータを **設定ファイルから読み込み、未指定時はデフォルト値にフォールバックする**形で公開する（see CSW-7, D-6 と同思想）。
3. The Camera Switcher Tab shall OSC メッセージのアドレスパターンに **cameraId を含む階層構造**（例: `/ucapi/camera/{cameraId}/flat`、具体パスは設計フェーズで確定）を採用し、1 本の OSC チャネルで複数カメラの transform ストリームを区別可能にする（see CSW-6）。
4. When 編集対象カメラの状態がフレーム単位で更新されたとき、the Camera Switcher Tab shall Requirement 3 のシリアライズ結果を OSC メッセージとして送信する。
5. The Camera Switcher Tab shall OSC 送信頻度の上限を **設計フェーズで確定する値**（目安 60 Hz 程度）に制限し、同一フレーム内の複数更新は 1 メッセージにまとめる（see CSW-9）。
6. The Camera Switcher Tab shall OSC を `core-ipc-foundation` の WebSocket/JSON 経路（D-5）とは **別チャネル** として扱い、transform 連続値を WebSocket の PublishState に載せない（see CSW-1）。
7. The Camera Switcher Tab shall OSC の送信ロールを **UI 側クライアント**（本タブ）、受信ロールを **メイン出力側サーバ**（受信実装は spec #2 output-renderer-shell の拡張またはアダプタ層）として固定する（see CSW-7）。
8. The Camera Switcher Tab shall OSC 送信クライアントを本タブの asmdef 内で管理し、本タブの初期化（UI アクティブ化後・cameraId 確定後）で起動、非アクティブ化・タブ破棄・PlayMode 終了で安全に停止する（see D-9 と同思想、Requirement 10）。
9. If OSC 送信クライアントの初期化に失敗した場合（ポート占有、ライブラリ不在等）, the Camera Switcher Tab shall 失敗事由を診断ログへ記録し、UI 側で「OSC 送信不可」状態を診断 API から取得可能にする（see CSW-15）。UI 本体および他タブの動作は継続させ、メイン出力描画を阻害しない。
10. While OSC 送信クライアントが送信不能な状態である間, the Camera Switcher Tab shall UI 側のカメラ CRUD 操作・Local Volume 編集・プリセット操作（いずれも WebSocket 経路）を通常通り受け付け、OSC 断が他機能に波及しない構造を維持する（see CSW-15）。
11. When カメラが delete されて cameraId が失効したとき、the Camera Switcher Tab shall 当該 cameraId 向けの OSC 送信を停止する。
12. When オペレーターが編集対象カメラを切り替えたとき、the Camera Switcher Tab shall OSC 送信先 cameraId を当該カメラへ切り替え、旧カメラへの送信を停止する（see Requirement 2 Acceptance Criteria 9）。
13. The Camera Switcher Tab shall OSC 受信側（メイン出力側）がまだ起動していない期間中でも、UDP 送信は原理的に送信を継続する（接続概念が弱いため）ことを許容し、受信側起動後の同期は最新値到着で回復することを前提に据える（see CSW-15）。

---

### Requirement 5: メイン出力側での OSC 受信と Camera 適用（適用側契約）

**Objective:** 配信運用者として、OSC 経由で送られてくる UCAPI Flat Record が、メイン出力側の各 Camera コンポーネントに確実に反映され、かつメイン出力描画フレームを阻害しない状態を得たい。そうすれば UI 側のカメラ操作が本番配信映像にリアルタイムに現れる。

**Note:** 本要件は docs/requirements.md §5.3.3 第 4 項（適用）を反映するが、実装そのものはメイン出力側（`output-renderer-shell` の拡張またはアダプタ層）の responsibility である。本要件は **メイン出力側への期待** を IPC/OSC 契約として明文化する。

#### Acceptance Criteria

1. The Camera Switcher Tab shall メイン出力側が本 spec の OSC アドレスパターン（Requirement 4 Acceptance Criteria 3）と UCAPI Flat Record（Requirement 3）を受信解釈できることを前提とする契約を、本要件および設計ドキュメントで明示する。
2. The Camera Switcher Tab shall メイン出力側受信ハンドラが cameraId ごとに対応する Unity Camera コンポーネントへ position / rotation / focal length / aspect 等を適用することを契約として要求する。
3. The Camera Switcher Tab shall メイン出力側の OSC 受信コールバックが **Unity メインスレッド上で Camera コンポーネントを更新する** ことを契約として要求する（see CSW-8, D-3 と同思想）。
4. The Camera Switcher Tab shall メイン出力側が受信キュー溢れ時に **同一 cameraId の最新値を優先** して採用することを契約として要求する（see CSW-9、D-7 の coalesce と同思想の OSC 側再現）。
5. If 受信した UCAPI Flat Record の CRC16-CCITT 検証に失敗した場合, the Camera Switcher Tab shall メイン出力側が当該メッセージを破棄して診断ログに記録し、以降の受信を継続することを契約として要求する（see Requirement 3 Acceptance Criteria 2）。
6. If 受信した cameraId がメイン出力側で未知であった場合, the Camera Switcher Tab shall メイン出力側が当該メッセージを破棄して診断ログに記録することを契約として要求する（カメラ create event より先に OSC が到着する起動レース等を想定）。
7. The Camera Switcher Tab shall メイン出力側が OSC 受信処理によって **メイン出力（Display 2+）の描画フレームを中断・遅延させない** ことを契約として要求する（see docs/requirements.md §4.2, §6.1）。
8. The Camera Switcher Tab shall カメラ active-set イベントを受けたメイン出力側が、対応するメイン出力ディスプレイの Camera を当該 cameraId のカメラへ切り替えることを契約として要求する（see CSW-12）。
9. The Camera Switcher Tab shall メイン出力側の受信実装が、**差し替え前提の最小 API 契約**（CSW-2）で定義されるプロトコル以外の暗黙の前提に依存しないよう、本要件で明示された IPC/OSC 契約のみを入力として動作するものとして設計されることを期待する。
10. Where 将来より高機能なカメラスイッチャーに差し替える場合, the Camera Switcher Tab shall 本要件の OSC 契約と IPC 契約を拡張（後方互換）または別契約として追加し、メイン出力側受信実装も契約追従で切替可能な構造を維持する（see CSW-2）。

---

### Requirement 6: カメラの動的管理（生成・削除・メタデータ・識別子採番）

**Objective:** 配信オペレーターとして、タブを開いている間に任意個数のカメラをシーンに追加・削除し、各カメラを識別して個別に編集・スイッチできる状態を得たい。そうすれば配信シーンに応じてカメラレイアウトを自在に構築できる。

**Note:** Camera GameObject はメイン出力側に常駐し所有権もメイン出力側（SL-2 / CS-1 と同思想）。UI 側は cameraId を握り、生成／削除は `PublishEvent`、メタデータ（名前・タイプ等）は `PublishState` で送る（CSW-4）。cameraId はメイン出力側が採番して生成完了イベントで UI 側に返却する（CSW-5）。transform 連続値は本 IPC 経路ではなく OSC で送る（Requirement 4）。

#### Acceptance Criteria

1. The Camera Switcher Tab shall メイン出力側から購読する **カメラ一覧 state**（topic 例: `cameras/list`、payload に cameraId 配列と各カメラのメタデータを含む）に基づいて、UI 上に現存するカメラのリストを表示する。
2. The Camera Switcher Tab shall カメラリスト UI 上で各カメラを **cameraId または表示名** で識別可能な形で列挙し、選択・編集対象切替・アクティブ切替・削除操作の起点を提供する。
3. When オペレーターがカメラの追加操作（`Add Camera` ボタン等）を実行したとき、the Camera Switcher Tab shall `ui-toolkit-shell` の Command 送信 API を介して **event コマンド**（topic 例: `camera/command`、payload に操作種別 `add` と初期カメラタイプ・任意のカメラ名を含む）を送信する（see CSW-4, CSW-10）。
4. When メイン出力側からカメラ生成完了イベント（採番された cameraId を含む）が通知されたとき、the Camera Switcher Tab shall 当該 cameraId を内部に保持し、UI のカメラリストに反映する（see CSW-5）。
5. When オペレーターがカメラの削除操作を実行したとき、the Camera Switcher Tab shall **event コマンド**（topic 例: `camera/command`、payload に操作種別 `delete` と対象 cameraId を含む）を送信し、UI リストから該当項目を（メイン出力側からの削除完了 state/event 通知後に）除去し、当該 cameraId への OSC 送信も停止する（see Requirement 4 Acceptance Criteria 11）。
6. The Camera Switcher Tab shall カメラメタデータ（表示名・カメラタイプ・初期 transform デフォルト等）の変更を **state コマンド**（topic 例: `camera/{cameraId}/metadata/{key}`、see SL-6 / CSW-4 のトピック粒度方針）で送信する。
7. While カメラ追加要求の送信から cameraId 付き完了イベントを待機している間, the Camera Switcher Tab shall UI 上に「生成中」プレースホルダを表示し、同一タイミングでの複数回連打による重複追加を構造的に抑止する（see CSW-5、SL-3 と同思想）。
8. If カメラ追加イベントの送信から一定時間（設計フェーズで確定）完了イベントが届かなかった場合, the Camera Switcher Tab shall 「生成中」プレースホルダをタイムアウト扱いにし、UI 上に警告を提示したうえでオペレーターが再試行できる状態に戻す（UI クラッシュを発生させない）。
9. The Camera Switcher Tab shall カメラ一覧の表示順を安定的な順序（例: cameraId の採番順、または表示名昇順）で固定し、state 更新のたびに順序が揺らがないようにする。
10. While カメラ一覧 state がまだメイン出力側から受信できていない間, the Camera Switcher Tab shall カメラリスト領域にプレースホルダまたは「接続待ち」表示を提示し、カメラ操作を非活性化する。
11. The Camera Switcher Tab shall カメラ作成時の初期 transform / 光学パラメータの既定値は **メイン出力側が定義する** ことを前提とし、UI 側からは数値を送らず、カメラタイプと任意のカメラ名のみを送る（see CSW-10）。
12. If メイン出力側からカメラ生成失敗イベント（リソース不足、不正タイプ等）が通知された場合, the Camera Switcher Tab shall 「生成中」プレースホルダをエラー表示に切り替え、失敗事由を UI 側診断領域で参照可能にする（UI クラッシュ・描画停止を発生させない）。

---

### Requirement 7: 簡易スイッチャー UI（アクティブカメラ切替、ハードカットのみ）

**Objective:** 配信オペレーターとして、登録されたカメラの中から配信映像に流すカメラを 1 ボタンで切り替えられる最小機能のスイッチャー UI を得たい。そうすれば本番配信中にリハーサル通りのカメラ切替が即座に行える。

**Note:** 本フェーズの切替は **ハードカット専用**（ディゾルブ・トランジションは非目標、docs/requirements.md §5.3.4）。active-set は離散操作として `PublishEvent` で送る（CSW-4）。アクティブカメラの現在値は `PublishState` で購読する。カメラ切替と連動した Local Volume 切替はメイン出力側が自動で行う（CSW-12）。本 UI は差し替え前提の最小機能であり、将来より高機能なスイッチャー（PVW/PGM、トランジション等）へ差し替えられる契約を維持する（CSW-2）。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 現在登録されている全カメラから 1 台を **アクティブ（配信に流す）カメラ** として指定するための UI（ボタン・リスト選択等）を提供する。
2. The Camera Switcher Tab shall メイン出力側から購読する **現在アクティブな cameraId の state**（topic 例: `cameras/active`）に基づいて、UI 上でアクティブカメラを視覚的に識別可能にする。
3. When オペレーターがアクティブカメラ切替操作を実行したとき、the Camera Switcher Tab shall `ui-toolkit-shell` の Command 送信 API を介して **event コマンド**（topic 例: `camera/command`、payload に操作種別 `active-set` と対象 cameraId を含む）を送信する（see CSW-4）。
4. The Camera Switcher Tab shall アクティブカメラの切替を **ハードカット専用** として扱い、ディゾルブ・カット補間等のトランジションを本タブで実装しない（docs/requirements.md §5.3.4 の非目標、see CSW-2）。
5. The Camera Switcher Tab shall アクティブカメラ切替に付随する **Local Volume の自動アクティブ化** は UI 側から別送せず、メイン出力側の契約（CSW-12）に委ねる。
6. While アクティブカメラ切替要求の送信からメイン出力側の適用完了通知（または更新された `cameras/active` state）を待機している間, the Camera Switcher Tab shall UI 上に進行中表示を提示し、同時刻での重複切替要求を抑止または直列化する。
7. If アクティブカメラ切替に対してメイン出力側から失敗イベント（指定 cameraId が存在しない等）が通知された場合, the Camera Switcher Tab shall UI 上に切替失敗を提示し、直前のアクティブカメラ状態を維持する（UI クラッシュ・描画停止を発生させない）。
8. The Camera Switcher Tab shall 本スイッチャー UI を **差し替え前提の最小 API 契約**（CSW-2）として構造化し、将来より高機能なスイッチャー（PVW/PGM、トランジション、外部ハードウェア連携等）に差し替えられるよう、`camera/command`（`active-set`）トピックに載せるコマンドの拡張余地（例: 後方互換フィールド追加による遷移種別の付加）を残す。
9. The Camera Switcher Tab shall カメラが 0 台である状態でアクティブ切替 UI を操作不能にし、オペレーターに「カメラを先に追加してください」といった誘導表示を提供する。
10. Where 将来 PVW/PGM の分離が必要となる場合, the Camera Switcher Tab shall 現在の「単一アクティブカメラ」契約を拡張点として維持し、本フェーズでは単一カメラ運用のみをサポートする（see CSW-2, docs/requirements.md §5.3.4）。

---

### Requirement 8: カメラごとの Local Volume 編集 UI

**Objective:** 配信オペレーターとして、各カメラに紐づく URP Local Volume（Bloom, DoF, Grain 等）を UI 上でメタデータ駆動に編集し、カメラ切替と連動して効果が切り替わる状態を得たい。そうすればカメラごとに固有のルック（シネマティックな DoF 付き / クリーンなワイドショット等）を事前に仕込めて、配信中の切替時に自動的に適用される。

**Note:** 本要件は docs/requirements.md §5.3.5 を反映する。Local Volume Override リストと各 Override の `enabled` / param 値は SL-7 の Global Volume と同じメタデータ駆動の枠組み（動的 UI 生成）で扱う（CSW-11）。連続値の param は `PublishState`、Override の enable/disable や追加削除は `PublishEvent` で送る（CSW-4）。カメラ切替と連動した Volume アクティブ化はメイン出力側の自動処理（CSW-12）。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 各カメラ（cameraId ごと）に紐づく **Local Volume** の情報をメイン出力側から購読し、UI 上に Override リストを表示する（topic 例: `camera/{cameraId}/volume/overrides`、具体名は設計フェーズで確定）。
2. The Camera Switcher Tab shall 利用可能な URP Volume Override 種別（Bloom, DoF, Tonemapping, ColorAdjustments, Grain 等）のメタデータを **IPC Request** で取得し、当該メタデータ（項目名・型・レンジ・既定値）に従って各 Override の param 編集 UI を **動的に生成** する（see CSW-11, CS-5 / SL-7 と同パターン）。
3. When オペレーターが Local Volume に新規 Override を追加したとき、the Camera Switcher Tab shall **event コマンド**（topic 例: `camera/{cameraId}/volume/command`、payload に操作種別 `override-add` と Override 種別を含む）を送信する（see CSW-4）。
4. When オペレーターが Local Volume から Override を削除したとき、the Camera Switcher Tab shall **event コマンド**（操作種別 `override-remove` と対象 Override 識別子を含む）を送信する（see CSW-4）。
5. When オペレーターが Override の `enabled` を切り替えたとき、the Camera Switcher Tab shall **state コマンド**（topic 例: `camera/{cameraId}/volume/override/{overrideType}/enabled`、see SL-6 / CSW-4）を送信する。
6. When オペレーターが Override の param 値（連続値）を変更したとき、the Camera Switcher Tab shall **state コマンド**（topic 例: `camera/{cameraId}/volume/override/{overrideType}/{param}`、see SL-6 / CSW-4）を送信する。
7. The Camera Switcher Tab shall Override param 編集 UI を `ui-toolkit-shell` の共通 UI コンポーネントライブラリ（UI-4: スライダー、カラーピッカー、トグルグループ等）から組み上げ、独自 UI 部品の重複実装を避ける（see UI-4、SL-7 と同方針）。
8. The Camera Switcher Tab shall Override param のバリデーション範囲（例: Bloom Intensity >= 0、Color の 0.0〜1.0 等）を UI 側でも一次バリデーションし、範囲外の送信を抑止する（see SL-12 と同思想）。
9. When オペレーターが Local Volume 全体の有効/無効を切り替えたとき、the Camera Switcher Tab shall **state コマンド**（topic 例: `camera/{cameraId}/volume/enabled`）を送信する。
10. When メイン出力側から Local Volume の param 現在値 state が通知されたとき、the Camera Switcher Tab shall UI 上の param コントロール表示を当該値に追従更新する（ただしオペレーターが操作中のコントロールの競合解消は設計フェーズで確定、SL-7 / CS-5 の R-6 と同種の課題）。
11. If Local Volume メタデータ取得 Request がタイムアウトまたは失敗した場合, the Camera Switcher Tab shall 対象カメラの Volume 編集領域にエラー表示を提示し、再試行 UI を提供したうえで、本タブ全体および他カメラの Volume 編集を阻害しない。
12. The Camera Switcher Tab shall **カメラ切替時の Local Volume 自動切替** を UI 側から明示送信せず、メイン出力側が active-set イベントを受けて対応する Local Volume を差し替える前提に据える（see CSW-12）。
13. When オペレーターが編集対象カメラを切り替えたとき、the Camera Switcher Tab shall Local Volume 編集 UI を新しい cameraId のメタデータに応じて再構成する（スキーマは共通だが、現在有効な Override 集合がカメラごとに異なり得ることを前提とする）。

---

### Requirement 9: カメラ切替と Local Volume 連動の契約

**Objective:** 配信オペレーターとして、カメラを切り替えたら対応する Local Volume の効果も自動的に切り替わる体験を得たい。そうすれば UI 上で「カメラ切替」と「Volume 切替」を別操作として意識せずに済み、誤操作・切替漏れが減る。

**Note:** 本要件は docs/requirements.md §5.3.5「カメラ切替時に、対応する Volume の効果が自動的に切り替わること」を反映する。UI 側は `camera/command` の `active-set` のみを送り、Local Volume アクティブ化はメイン出力側が付随的に行う（CSW-12）。本要件はメイン出力側受信実装への契約を明文化する。

#### Acceptance Criteria

1. The Camera Switcher Tab shall カメラ切替時の Local Volume 連動をメイン出力側の自動処理として委ね、UI 側は `camera/command` の `active-set`（Requirement 7 Acceptance Criteria 3）のみを送信する（see CSW-12）。
2. The Camera Switcher Tab shall メイン出力側が active-set イベントを受けたときに、**対象 cameraId の Local Volume を有効化、他カメラの Local Volume を無効化する** ことを契約として要求する（see CSW-12、docs/requirements.md §5.3.5）。
3. The Camera Switcher Tab shall Local Volume の有効/無効状態を `camera/{cameraId}/volume/enabled` state（Requirement 8 Acceptance Criteria 9）として購読することで、メイン出力側の自動切替結果を UI 上に反映する。
4. If オペレーターが手動で Local Volume の `enabled` を切り替えた後にカメラ切替を行った場合の優先順位（手動操作 vs 自動切替）は設計フェーズで確定する契約として要件に明記する。本フェーズの既定挙動は「active-set 時にメイン出力側が全カメラの Local Volume を再評価し、アクティブカメラのみ有効化する」ものとする（see CSW-12）。
5. Where 将来より高機能なスイッチャーが Volume の手動制御を厳密に必要とする場合, the Camera Switcher Tab shall 本要件の自動連動契約を後方互換で拡張（例: 手動制御モードのフラグ追加）できる構造を残す（see CSW-2）。
6. The Camera Switcher Tab shall UI 上で「カメラ切替」と「Local Volume 連動の自動切替」の関係をオペレーターが理解できるよう、関連するドキュメントまたは UI 上のヒント表示を提供する（具体 UX は設計フェーズで確定）。

---

### Requirement 10: OSC 送信クライアントのライフサイクル管理

**Objective:** システム運用者として、OSC 送信クライアントがアプリ起動・終了・PlayMode 開始終了に合わせて適切に初期化・解放され、リソースリークやポート占有残存が発生しない状態を得たい。そうすれば開発中の PlayMode 繰り返しや本番運用での再起動が安全に行える。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。OSC ライブラリの具体 API は設計フェーズで選定・確定する。

#### Acceptance Criteria

1. When 本タブがアクティブ化され、かつ `core-ipc-foundation` の IPC 接続が確立済みであるとき、the Camera Switcher Tab shall OSC 送信クライアントを初期化し、設定ファイルまたはデフォルトの接続先ホスト・ポートに送信可能な状態とする（see Requirement 4 Acceptance Criteria 2）。
2. When Unity Editor が PlayMode を終了したとき、the Camera Switcher Tab shall OSC 送信クライアントを完全にシャットダウンし、ソケット・スレッド・メモリ等のリソースを解放する（see D-9）。
3. When Unity アプリケーションが終了要求を受けたとき、the Camera Switcher Tab shall OSC 送信クライアントを安全にシャットダウンし、未送信メッセージの扱いを定義済みの方針（基本は破棄）に従って処理する。
4. While PlayMode の開始と停止が繰り返される間, the Camera Switcher Tab shall OSC ポート占有やスレッドリークを発生させずに毎回クリーンに再初期化する。
5. The Camera Switcher Tab shall Unity Editor の **Edit モード** では OSC 送信クライアントを起動しない（see D-9）。
6. The Camera Switcher Tab shall ドメインリロードに跨る OSC クライアント状態維持を試みず、PlayMode 開始のたびに新しいライフサイクルで初期化する（see D-9）。
7. If OSC 送信クライアントの初期化中に例外が発生した場合, the Camera Switcher Tab shall 例外を捕捉して診断ログに記録し、UI 本体の起動と他機能（WebSocket 経路のカメラ CRUD 等）の動作を継続させる（see CSW-15）。
8. The Camera Switcher Tab shall OSC 送信クライアントのスレッド実装（I/O ワーカー）とメインスレッドディスパッチを、`core-ipc-foundation` の D-3 契約と同一のスレッドモデル（ワーカー I/O → SynchronizationContext 経由でメインスレッドコールバック）で実装する（see CSW-8）。

---

### Requirement 11: 名前付きプリセット（カメラリスト + Local Volume + アクティブカメラ）

**Objective:** 配信オペレーターとして、配信シーンごと（朝配信、コラボ配信、アクション配信等）のカメラレイアウト + 各カメラの初期 transform + Local Volume 構成 + アクティブカメラを「1 単位」として保存し、プリセット選択 1 回で一括適用できる状態を得たい。そうすれば配信準備のたびに複数カメラを 1 つずつ設定する手間を省ける。

**Note:** 本要件は CS-12 / SL-8 と同構造の「名前付きプリセット複数保持」パターンを踏襲する（CSW-13）。永続化タイミングはデバウンス即時保存（CSW-14、CS-9 / SL-9 と同一方針）、復元は通常 state/event 経路で一括送信する（CS-10 / SL-8 と同一パターン）。カメラ transform 連続値（OSC で送られるストリーム）は永続化対象外、初期 transform デフォルトのみがプリセットの一部として保存される。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 永続化対象を **「カメラリスト（cameraId + カメラタイプ + 表示名 + 初期 transform デフォルト）」** および **「各カメラの Local Volume 構成（Override 種別・enabled・param 値）」** および **「アクティブカメラの cameraId」** とし、これらを **名前付きプリセット単位** で保存する（see CSW-13, CS-8 と同思想）。
1a. The Camera Switcher Tab shall 複数のプリセットを保持し、オペレーターが以下のプリセット操作を UI から実行できるようにする：**新規作成（create）**、**名前変更（rename）**、**複製（duplicate）**、**削除（delete）**、**アクティブ化（activate/switch）**（see CSW-13, CS-12, SL-8）。
1b. The Camera Switcher Tab shall 現在アクティブなプリセット名を UI 上に明示的に表示する（see CSW-13）。
1c. When オペレーターがアクティブプリセットを切り替えたとき、the Camera Switcher Tab shall 切替先プリセットの内容を **通常の state/event コマンド経路**（Requirement 6, 7, 8）で送信し、メイン出力側のカメラリスト・Local Volume・アクティブカメラを一括適用する（see CSW-13, CS-10 と同方針）。
1d. If プリセット新規作成時に既存プリセットと重複する名前が指定された場合, the Camera Switcher Tab shall 作成を拒否してバリデーションエラーを UI に表示する（see CSW-13, CS-12 と同方針）。
2. The Camera Switcher Tab shall 永続化対象外とするもの（OSC で送られる transform 連続値、編集対象カメラ選択状態、プレビュー視点、タブ切替状態、ウィンドウ配置等）を保存ファイルに含めない（see CSW-14, SL-10 と同思想、UI-7）。
3. When 永続化対象の値（カメラ追加削除、メタデータ変更、Local Volume の state 変化、アクティブカメラ変更）が変更されたとき、the Camera Switcher Tab shall 変更を内部バッファに蓄積し、**デバウンス**（具体値は設計フェーズで確定）経過後にファイルへフラッシュする（see CSW-14, CS-9 / SL-9 と同方針）。
4. When Unity アプリケーションが正常終了（スタンドアロンの OnApplicationQuit、PlayMode 停止等）を迎えたとき、the Camera Switcher Tab shall 保留中の未フラッシュ変更をファイルへ書き出す（see CSW-14, D-9）。
5. When 本タブが起動し、`core-ipc-foundation` の IPC 接続が確立したとき、the Camera Switcher Tab shall 保存ファイルからアクティブプリセットを読み込み、カメラの生成・メタデータ設定・Local Volume 設定・アクティブカメラ切替を **通常の state/event コマンド経路**（Requirement 6, 7, 8）で送信して復元する（see CSW-13, CS-10 / SL-8 と同方針）。
6. If 保存ファイルが存在しない（初回起動）場合, the Camera Switcher Tab shall 復元処理をスキップし、メイン出力側の現在状態（デフォルトカメラのみ、OR Requirement 1.2）をそのまま UI に反映する。
7. If 保存ファイルの読み込みまたはパースに失敗した場合, the Camera Switcher Tab shall エラーを診断ログに記録し、破損ファイルをバックアップ（リネーム等）したうえで初回起動扱いにフォールバックする（see CS-11 と同思想）。
8. If プリセット復元中に一部カメラの生成・メタデータ設定・Local Volume 設定の送信が失敗した場合, the Camera Switcher Tab shall 失敗項目のみエラー表示で UI に反映し、他カメラ・他 Volume の復元は継続する（see CS-11 と同思想）。
9. If 保存ファイル書き込みに失敗した場合（ディスク容量不足、権限エラー等）, the Camera Switcher Tab shall エラーを診断ログに記録し、UI 上に保存失敗通知を提示したうえで次回変更時に再試行する（UI クラッシュ・描画停止を発生させない）。
10. The Camera Switcher Tab shall 保存ファイルの配置・フォーマットを設計フェーズで確定することを要件として明記し、利用者プロジェクトでの配置場所（Application.persistentDataPath 配下等）が差し替え可能な構造を維持する。
11. When プリセット切替中に既存カメラの delete と新規カメラの add が多数発生する場合, the Camera Switcher Tab shall 送信順序（例: 旧カメラの delete → 新カメラの add → メタデータ設定 → Local Volume 設定 → active-set）を設計フェーズで確定する契約として定め、中間状態が配信映像を不自然に乱さない配慮を行う（see CSW-13, SL-8 と同種の課題）。

---

### Requirement 12: 失敗・縮退ハンドリングとフェイルセーフ

**Objective:** 配信オペレーターとして、OSC 送信失敗、Local Volume メタデータ取得失敗、カメラ生成失敗、範囲外パラメータ、メイン出力側からのエラー event 等の異常時でも、タブや UI シェル全体がクラッシュせず、問題箇所だけが縮退表示される状態を得たい。そうすれば配信中の部分的な障害が全体停止に発展せず、運用継続性を確保できる。

**Note:** 本要件は `output-renderer-shell` Requirement 5（メイン出力描画にエラー UI を出さない）、`ui-toolkit-shell` Requirement 9（フェイルセーフ）、CS-11 / SL-11（不可用アセット時の縮退）と整合する。OSC 断と WebSocket 断は独立した事象として扱い、どちらか片方が生きていれば可能な範囲で機能を継続する（CSW-15）。

#### Acceptance Criteria

1. If OSC 送信クライアントの初期化または送信に失敗した場合, the Camera Switcher Tab shall 失敗事由を診断ログに記録し、UI 側で「OSC 送信不可」状態を診断 API から取得可能にしたうえで、WebSocket 経路（カメラ CRUD・Local Volume 編集・プリセット操作）の動作を継続する（see CSW-15, Requirement 4 Acceptance Criteria 9, 10）。
2. If Local Volume メタデータ取得 Request がタイムアウトまたは失敗した場合, the Camera Switcher Tab shall 対象カメラの Volume 編集 UI のみをエラー表示に切り替え、本タブ全体および他カメラの Volume 編集・カメラ CRUD の動作を継続する（see Requirement 8 Acceptance Criteria 11）。
3. If メイン出力側からカメラ関連のエラー event（生成失敗、削除失敗、active-set 失敗等）が通知された場合, the Camera Switcher Tab shall 該当カメラまたは該当操作の UI 要素をエラー表示に切り替え、他カメラの表示・操作を継続する（see CS-11 / SL-11 と同思想）。
4. If Command 送信 API が接続未確立エラーまたはサイズ上限超過エラー（D-11）を返した場合, the Camera Switcher Tab shall エラーを UI 側診断領域に記録し、UI クラッシュ・描画停止を発生させない（see UI-5）。
5. If 設定値バリデーションで範囲外入力が発生した場合, the Camera Switcher Tab shall 送信を抑止し、UI 上でバリデーションエラーを該当コントロール近傍に表示する（see Requirement 8 Acceptance Criteria 8, SL-12 と同思想）。
6. The Camera Switcher Tab shall いかなる失敗経路においても、メイン出力（Display 2+）へ警告・エラー UI を描画しない（see OR-1 の UI 側責務、ui-toolkit-shell Requirement 11 第 7 項）。
7. While メイン出力側との IPC（WebSocket）接続が切断している間, the Camera Switcher Tab shall カメラ CRUD・Local Volume 編集 UI を安全に非活性化または保留状態に切り替え、接続回復後に復帰可能な状態を維持する（see ui-toolkit-shell Requirement 9）。OSC 送信は UDP 特性上独立に継続するが、受信側が切れている可能性があるため UI 上に診断状態として露出する。
8. When IPC（WebSocket）接続が回復したとき、the Camera Switcher Tab shall カメラ一覧・Local Volume 構成・アクティブカメラを再取得し、UI を現時点のメイン出力側状態に同期する（see CS-7）。
9. If 保存プリセットが参照するメタデータや cameraId がメイン出力側の現実と整合しない（過去のバージョンからのマイグレーション不整合等）場合, the Camera Switcher Tab shall 該当プリセット項目のみ警告表示で UI に露出し、他プリセット・他カメラの復元を継続する（see CS-11 / SL-11 と同思想、Requirement 11 Acceptance Criteria 8）。
10. The Camera Switcher Tab shall 失敗経路で発生した診断情報（失敗トピック・対象 cameraId・失敗事由）をログ出力し、UI 側診断領域からも参照可能にする（see Requirement 14）。

---

### Requirement 13: スタンドアロンビルドと Unity Editor PlayMode の両対応

**Objective:** 配信運用者および開発者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で、本タブの挙動（UI 表示、カメラ CRUD、OSC 送信、Local Volume 編集、プリセット永続化）が同一であることを得たい。そうすれば開発中の検証と本番運用の挙動差を最小化でき、Editor PlayMode で配信前リハーサルが完結する。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。ドメインリロードに跨る状態維持は試みない。OSC 送信クライアントのライフサイクルは Requirement 10 と整合する。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動し、`ui-toolkit-shell` のプリロードが完了したとき、the Camera Switcher Tab shall 本タブの UI 初期化・カメラ一覧購読・プリセット復元・OSC 送信クライアント初期化を自動的に実施する（see Requirement 10 Acceptance Criteria 1）。
2. When Unity Editor が PlayMode に入ったとき、the Camera Switcher Tab shall スタンドアロン時と同一手順で UI 初期化・カメラ一覧購読・プリセット復元・OSC 送信クライアント初期化を実施する（see D-9, Requirement 10）。
3. When Unity Editor が PlayMode を終了したとき、the Camera Switcher Tab shall 保留中の未フラッシュ永続化データを書き出し、OSC 送信クライアントをシャットダウンし、購読を解除し、内部状態をクリーンアップして Edit モードに残留物を残さない（see D-9, Requirement 10 Acceptance Criteria 2, Requirement 11 Acceptance Criteria 4）。
4. While PlayMode の開始と停止が繰り返される間, the Camera Switcher Tab shall 購読重複・UI 要素重複生成・OSC ポート占有残存・永続化ファイルのロック残存を発生させず、毎回クリーンに再初期化する（see Requirement 10 Acceptance Criteria 4）。
5. The Camera Switcher Tab shall Unity Editor の **Edit モード** では本タブの実行時ロジック（UI 初期化、IPC 購読、OSC 送信、永続化読込等）を起動しない（see D-9, Requirement 10 Acceptance Criteria 5）。
6. The Camera Switcher Tab shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに永続化ファイルから復元する（see D-9, Requirement 11 Acceptance Criteria 5）。
7. The Camera Switcher Tab shall スタンドアロン時と Editor PlayMode 時で、オペレーターから見た UI 挙動・OSC 送信の到達性（ローカルループバック）・永続化挙動を同一に保つ。

---

### Requirement 14: 観測性・診断可能性

**Objective:** 開発者・配信運用者として、本タブで発生する不具合が「UI 起因」「IPC（WebSocket）送受信起因」「OSC 送信起因」「UCAPI シリアライズ起因」「Local Volume メタデータ起因」「永続化 I/O 起因」のいずれかを即座に切り分けたい。そうすれば問題切り分けに要する時間を最小化し、本番配信中でも迅速に対応できる。

**Note:** 本要件の診断出力は `output-renderer-shell` Requirement 5 および `ui-toolkit-shell` Requirement 11 第 7 項に従い、UI 側（Display 1）またはコンソールへのみ流し、メイン出力（Display 2+）に一切描画しない。

#### Acceptance Criteria

1. The Camera Switcher Tab shall 本タブの初期化・UIDocument アタッチ完了・カメラ一覧購読登録・Local Volume メタデータ取得・OSC 送信クライアント初期化・永続化読込の各段階の開始・完了・失敗をログ出力する。
2. The Camera Switcher Tab shall カメラ CRUD 操作（create / delete の event 送信、メタデータ state 送信、active-set の event 送信）の送信元 cameraId・topic・送信時刻をログレベルに応じて出力する。
3. The Camera Switcher Tab shall Local Volume 編集（Override add/remove の event 送信、enabled/param state 送信）の対象 cameraId・Override 種別・送信時刻をログ出力する。
4. The Camera Switcher Tab shall OSC 送信イベント（送信件数、送信失敗、宛先ホスト・ポート、cameraId ごとの送信頻度等、具体項目は設計フェーズで確定）を診断 API およびログレベルに応じた出力で公開する。
5. When メイン出力側からカメラ関連のエラー event を受信したとき、the Camera Switcher Tab shall 対象 cameraId・エラー種別・受信時刻を診断ログに記録する。
6. When 永続化ファイルの読込・書込に失敗したとき、the Camera Switcher Tab shall ファイル識別子・失敗事由を診断ログに記録する（see Requirement 11）。
7. The Camera Switcher Tab shall 診断ログを Unity コンソールまたは UI 側診断領域（`ui-toolkit-shell` が提供するもの）にのみ流し、メイン出力サーフェスへ一切描画しない（see OR Requirement 5, UI Requirement 11 第 7 項）。
8. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the Camera Switcher Tab shall ログレベルを外部から切替可能にする（`ui-toolkit-shell` Requirement 11 第 8 項と整合）。
9. The Camera Switcher Tab shall 診断に必要な最小限の状態（現在のカメラ数、アクティブカメラ cameraId、編集対象カメラ cameraId、OSC 送信クライアント状態、OSC 送信頻度、IPC 接続状態、永続化最終保存時刻、現在アクティブプリセット名）を外部から取得可能な形で公開する。

---

### Requirement 15: 本 spec 単体での検証可能性

**Objective:** spec オーナーとして、本タブを `output-renderer-shell` 側の Camera / Local Volume / OSC 受信アダプタ実装がそろう前に検証したい。そうすれば Wave 3 の 3 タブを並行開発する際に、モックを介して本タブの UI・IPC 契約・OSC 契約を独立に検証できる。

**Note:** 本要件は `ui-toolkit-shell` Requirement 10（単体検証）と `core-ipc-foundation` Requirement 8（自己ループ）を活用する。OSC 受信側はループバック受信ダブル（テスト用 OSC リスナー）で受信内容を確認可能にする。UCAPI4Unity は実パッケージを使用し、Flat Record シリアライズの整合性は本 spec 側のテストで検証する。

#### Acceptance Criteria

1. The Camera Switcher Tab shall メイン出力側の Camera / Local Volume 実接続実装がなくても、IPC 契約上のモック応答（カメラ一覧、Local Volume メタデータ、state 反映応答、エラー event、cameraId 発行通知等）を与えるテストダブルと連携して、UI の全表示・操作経路を実行できる構造とする（see CS-11 / SL の対応要件と同思想）。
2. The Camera Switcher Tab shall `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）と `ui-toolkit-shell` のモック受容構造（UI Requirement 10 第 6 項）を利用して、Command 送信 API 経由の state / event / request を自プロセス内で送受信するテストケースを提供する。
3. The Camera Switcher Tab shall **OSC 受信側のテストダブル**（同一プロセス内のループバック受信、または簡易 OSC リスナー）を受け入れる構造を備え、UCAPI Flat Record が正しいアドレスパターン・バイト列・送信頻度で届くことを検証可能にする。
4. The Camera Switcher Tab shall 永続化 I/O 部分について、ファイルシステムに依存しない差し替え可能なストレージ抽象（メモリ上ダブル等）を受け入れる構造を備える（see CS-11 / SL の対応要件と同思想）。
5. The Camera Switcher Tab shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物、モックカメラ一覧・モック Local Volume メタデータ・ループバック OSC 受信を含む）を提供する。
6. When 本タブ単体のテスト実行が行われたとき、the Camera Switcher Tab shall 次の挙動を検証するテストケースを提供する：カメラ一覧 state の UI 反映、カメラ create/delete/active-set event 送信、メタデータ state 送信、Local Volume Override 動的 UI 生成、Override enable/param state 送信、UCAPI Flat Record シリアライズの整合性（CRC16-CCITT 含む）、OSC 送信の cameraId 区別、アクティブカメラ切替、プリセット CRUD と切替時の一括適用、OSC 断時のフェイルセーフ、IPC 切断中のフェイルセーフ、永続化の書込・読込・破損フォールバック。
7. The Camera Switcher Tab shall テスト時に OSC ライブラリの代わりに差し替え可能な OSC 送信抽象（送信内容をバッファに積むフェイク等）を受け入れる構造を備える。
8. The Camera Switcher Tab shall テスト時に時刻（デバウンスタイマー、タイムアウト、OSC 送信頻度制限等）を制御可能にするための時刻抽象を受け入れる構造を備える（設計フェーズで具体 API を確定、see CS Requirement 11 第 7 項と同思想）。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選、上流 spec 決定の積極的継承）
- **本 spec 固有の決定**: 15 件（CSW-1〜CSW-15）
- **継承**:
  - `core-ipc-foundation` の D-1（単一 Unity アプリ + LocalHost）、D-3（メインスレッド配信）、D-4（UI クライアント）、D-5（JSON / WebSocket）、D-6（設定ファイル + デフォルト）、D-7（state coalesce / event FIFO）、D-8（Request タイムアウト）、D-9（PlayMode のみ常駐）、D-10（PublishState / PublishEvent）、D-11（1 MB 上限）
  - `output-renderer-shell` の OR-1（Display 2 フォールバック時の UI 警告）、OR-2（state 競合 last-write-wins）
  - `ui-toolkit-shell` の UI-1（起動時プリロード）、UI-2（表示/非表示切替）、UI-3（USS 差し替え）、UI-4（共通 UI コンポーネントライブラリ）、UI-5（Command 送信 API）、UI-6（Addressables）、UI-7（タブ共通 UI 状態永続化なし）
  - `character-selection-tab` の CS-5（メタデータ駆動動的 UI）、CS-7（トピック粒度）、CS-9（デバウンス即時保存）、CS-10（通常 state 経路での復元）、CS-11（不可用アセット時の縮退）、CS-12（名前付きプリセット複数保持）、CS-13（ストアドサムネイル方針）
  - `stage-lighting-volume-tab` の SL-1（プレビュー UI 内埋め込み）、SL-3（ID 採番はサーバ側）、SL-6（トピック粒度）、SL-7（Volume Override のメタデータ駆動編集）、SL-8（名前付きプリセット）、SL-10（プレビュー視点は永続化しない）、SL-11（ロード失敗時の縮退）、SL-12（UI 側バリデーション）

- **主要な発見（本 spec 固有）**:
  - カメラ transform の連続値は **OSC 別チャネル**、UI 操作（create/delete/active-set、Local Volume、メタデータ、プリセット）は **WebSocket/JSON（core-ipc-foundation）** という **二系統のチャネル構成** を要件レベルで固定（CSW-1）。WebSocket 側の PublishState と OSC 側ストリームの役割分担を明確化し、設計時の混乱を防ぐ。
  - カメラスイッチャーが差し替え前提（docs/requirements.md §5.3.4）であることを **CSW-2 で契約境界として要件化**。本タブの API 契約（OSC アドレス、IPC トピック、プリセット構造、Local Volume 編集 UI）を「最小 API」として切り出し、将来のより高機能なスイッチャー（PVW/PGM、トランジション、タイムライン連携等）が同契約を拡張または別契約で差し替え可能な構造を維持する。
  - UCAPI（128 byte Flat Record + 10 byte Header + CRC16-CCITT + MessagePack 対応）の採用を要件レベルで固定（Requirement 3）。Unity Camera → UCAPI の単位変換、NaN/Inf のガード、UCAPI4Unity API 変更への追従構造を明示。
  - OSC のアドレスは **cameraId 階層化**（`/ucapi/camera/{cameraId}/flat`）で 1 本のチャネルに複数カメラを多重化（CSW-6）。将来のマルチカメラ多重ストリームや PVW/PGM 拡張に自然に接続できる。
  - **カメラ切替と Local Volume 連動は UI 側が明示送信せず、メイン出力側の自動処理**（CSW-12）。UI 側は `camera/command` の `active-set` を送るだけで、Local Volume の有効/無効は付随的に切り替わる。これにより UI と受信側で同期漏れが発生しない構造となる。
  - プリセット設計は CS-12 / SL-8 の名前付きプリセットパターンを踏襲し、**「カメラリスト + 各カメラ初期 transform デフォルト + Local Volume 構成 + アクティブカメラ」** を 1 単位として扱う（CSW-13）。OSC で送られる transform 連続値ストリーム自体は永続化対象外（CSW-14）。
  - OSC は UDP 前提のため **接続断の検出が原理的に弱い**（CSW-15）。OSC 断と WebSocket 断は独立した事象として扱い、WebSocket 側が生きている限り UI 操作は継続可能とする。OSC 送信クライアントは PlayMode のライフサイクル（D-9）と整合させ、ポート占有残存を防ぐ（Requirement 10）。

- **残留リスク（設計フェーズで継続検討）**:
  - R-CSW-1: OSC のデフォルトポート番号選定（UCAPI 標準ポートがあればそれを踏襲、未定義なら dynamic/private 範囲から選定）。他 VTuber 系ツールや DAW / OSC 対応ソフトとの衝突可能性を確認する。
  - R-CSW-2: OSC アドレスパターンの具体パス（`/ucapi/camera/{cameraId}/flat` は仮置き）。UCAPI 側のアドレス規約を設計フェーズで確認し、整合性を取る。
  - R-CSW-3: OSC 送信頻度上限の具体値（目安 60 Hz）と、UI フレームとの同期方式（Update / LateUpdate / 明示的タイマー）。
  - R-CSW-4: UCAPI Flat Record の具体項目と Unity Camera パラメータのマッピング。単位変換（メートル / Unity units、度 / ラジアン等）、座標系（左手 / 右手）の差異の棚卸し。
  - R-CSW-5: Unity 向け OSC ライブラリの選定（候補: uOSC, OscCore, extOSC, 自作等）。パフォーマンス、Unity 6.3 対応、ライセンス、スレッドモデルの評価。
  - R-CSW-6: プレビュー UI の表示方式（シングルプレビュー + 編集対象カメラ切替 / マルチプレビュー）の UX 確定。シングルなら UI と GPU コストが最小だが、複数カメラの事前確認ができない。
  - R-CSW-7: メイン出力側 Camera ルートと本タブで生成するカメラの関係（OR Requirement 1.2 の「デフォルトカメラ」を 1 台目扱いにするか、本タブで追加した時点で別扱いにするか）。
  - R-CSW-8: カメラ作成時の初期 transform 既定値の決定ルール（CSW-10）。現状案は「メイン出力側が定義」だが、オペレーターが使いやすいデフォルト視点（ステージ全体を映す等）を具体化する必要あり。
  - R-CSW-9: プリセット切替時の送信順序（Requirement 11 Acceptance Criteria 11）。旧カメラ delete → 新カメラ add → メタデータ → Local Volume → active-set の順序の具体化と、切替中の配信映像の「中間状態」の扱い（ブラックアウト、直前カメラ継続等）。
  - R-CSW-10: Local Volume メタデータ取得 API（CSW-11）の具体スキーマ。URP の VolumeComponent 公開 API を設計フェーズで棚卸し、UI 側で必要な項目（型・レンジ・既定値・表示名・単位等）が揃うかを確認する。
  - R-CSW-11: OSC 受信側（メイン出力側）実装の所在。`output-renderer-shell` の拡張か、本 spec 側の「メイン出力側アダプタ」として切り出すか（設計フェーズで切り分け）。
  - R-CSW-12: UCAPI4Unity のバージョン固定（docs/requirements.md §6.4）。C++ DLL の配布形態（Addressables / パッケージ同梱 / Native Plugin フォルダ）と利用者側インストール手順の整理。
  - R-CSW-13: 差し替え前提の最小 API 契約（CSW-2）の粒度。将来の高機能スイッチャーの仕様が未確定であるため、最低限「cameraId + active-set」「OSC アドレス」「Local Volume 連動契約」を壊さないことを設計フェーズで合意する。
  - R-CSW-14: 編集対象カメラとアクティブカメラの UX 分離方針。同一扱いにするか（操作 = 放送）、別扱いにするか（オペレーターは非アクティブカメラを事前に仕込める）。後者の方が運用柔軟性が高いが、UI の状態管理が複雑化する。
  - R-CSW-15: OSC 側の時刻同期（UCAPI のタイムコード運用）とメイン出力側の補間・フィルタ方針。UDP ロス時の「最新値到着で回復」以上の高度な補間は本フェーズ非目標だが、将来の PVW/PGM 拡張で必要になるため契約に残す。

## Dig Summary（本 spec 固有の追加分）

- **本 spec 固有の追加決定**: CSW-16（プレビュー UI はマルチプレビュー + 大アクティブカメラの二層構成）、CSW-9 を「メイン出力描画フレーム同期（実質 60 Hz）」へ具体化
- **継承**: core-ipc-foundation の D-1〜D-11、output-renderer-shell の OR-1, OR-2、ui-toolkit-shell の UI-1〜UI-7、character-selection-tab の CS-12（プリセット）、stage-lighting-volume-tab の SL-6（トピック粒度）SL-8（プリセット）SL-7（Volume 動的 UI）
- **残留リスク追加**:
  - R-CSW-16-1: N 台カメラのマルチプレビュー RenderTexture による GPU 負荷上限。サムネイルの解像度・更新頻度・最大カメラ数を設計フェーズで最適化
  - R-CSW-9-2: メイン出力描画フレームと UI プレビュー描画フレームの関係（同一 Update / 独立 Update）は設計フェーズで決定
