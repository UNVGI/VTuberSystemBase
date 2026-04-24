# Research & Design Decisions

## Summary

- **Feature**: `core-ipc-foundation`
- **Discovery Scope**: New Feature（Wave 1 基盤 spec、他の全 spec が本 spec に依存）
- **Key Findings**:
  - Unity は `HttpListener.AcceptWebSocketAsync` を実装していないため、WebSocket **サーバ**側を `System.Net.WebSockets` の `HttpListener` 経由で作ることは不可能である。サーバは `TcpListener` + 手書き RFC 6455 ハンドシェイク、または Fleck/websocket-sharp 等のサードパーティ実装が必要。
  - WebSocket **クライアント**側は `System.Net.WebSockets.ClientWebSocket` が Unity/Mono 上で機能するため、追加依存なしで実装可能。
  - Unity 6.3 では `UnitySynchronizationContext` がメインスレッドに紐付いており、`PlayerLoop` 経由でメインスレッドへ再ディスパッチできる。本 spec のスレッド契約（D-3）はこの機構で実現する。
  - JSON シリアライゼーションは `System.Text.Json`（Unity 6.3 / .NET Standard 2.1 想定）を一次採用する。`JsonUtility` は Dictionary 非対応かつ未知フィールド処理が弱いため、前方互換性（Req 3.7）の要件を満たせない。

## Research Log

### Topic 1: Unity における WebSocket サーバ実装の選択肢

- **Context**: D-4 により本基盤はメイン出力側を WebSocket **サーバ**、UI 側を WebSocket **クライアント**として起動する。Unity ランタイム上でサーバを安定して動かすための実装を確定する必要がある。
- **Sources Consulted**:
  - [RFC 6455 - The WebSocket Protocol](https://datatracker.ietf.org/doc/html/rfc6455)
  - [HttpListener and Websockets - Unity Discussions](https://discussions.unity.com/t/httplistener-and-websockets/820948)
  - [GitHub - sta/websocket-sharp](https://github.com/sta/websocket-sharp)
  - [GitHub - statianzo/Fleck](https://github.com/statianzo/Fleck)
  - [GitHub - endel/NativeWebSocket](https://github.com/endel/NativeWebSocket)
- **Findings**:
  - Unity の `HttpListener` 実装では `IsWebSocketRequest` が常に `false` を返し、`AcceptWebSocketAsync` は `NotImplementedException` を投げる。素の .NET と挙動が異なるため、サーバを `HttpListener.AcceptWebSocketAsync` 経由で作ることはできない。
  - 代替として (a) `TcpListener` + 手書き RFC 6455 アップグレードハンドシェイク（Sec-WebSocket-Accept 計算：`Sec-WebSocket-Key` + GUID `258EAFA5-E914-47DA-95CA-C5AB0DC85B11` を SHA-1 → Base64）、(b) Fleck（MIT、純粋マネージド、`HttpListener` 非依存）、(c) websocket-sharp（MIT、Unity 実績多数）の 3 択がある。
  - PlayMode 停止時にリスナースレッドが残存しポート占有するため、`EditorApplication.playModeStateChanged` フックでの明示停止が必須。
- **Implications**:
  - トランスポート層は抽象化するが、具体実装は「Unity ランタイム上で動作するサーバ機構」を自前で持つ必要がある。Fleck に依存すると UPM パッケージ配布時の NuGet 統合が煩雑になるため、**サーバ側は `TcpListener` + 手書きハンドシェイク**で実装する（外部依存ゼロ）。クライアント側は `System.Net.WebSockets.ClientWebSocket` を利用する。
  - 本 spec のサーバ実装は RFC 6455 の最小サブセットに限定する（テキストフレームのみ、`permessage-deflate` 拡張不使用、認証不要、サブプロトコルなし）。継続フレーム（fragmented frames）と control frame（ping/pong/close）の 3 種は必須実装。
  - サーバ／クライアントとも PlayMode 停止時の確実なリソース解放を `ICoreIpcLifecycle` インタフェースに組み込む（D-9 の要件）。

### Topic 2: Unity メインスレッドディスパッチ機構

- **Context**: D-3 により受信コールバック・Response 返却は常に Unity メインスレッド上で実行される必要がある。I/O はワーカースレッドで処理し、完了通知だけをメインスレッドに戻す。
- **Sources Consulted**:
  - [Unity Manual - Awaitable completion and continuation (Unity 6)](https://docs.unity3d.com/6000.3/Documentation/Manual/async-awaitable-continuations.html)
  - [Understanding SynchronizationContext and UnitySynchronizationContext](https://discussions.unity.com/t/understanding-synchronizationcontext-and-unitysynchronizationcontext/1700147)
  - [UnitySynchronizationContext.cs - Unity-Technologies/UnityCsReference](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs)
  - [Unity Main Thread Dispatcher - gustavopsantos](https://github.com/gustavopsantos/UnityMainThreadDispatcher)
- **Findings**:
  - Unity 6.3 は `UnitySynchronizationContext` をメインスレッドにインストールしている。`Task` 継続は既定でメインスレッドへ再ディスパッチされる。
  - より低レベルには `PlayerLoopSystem` の挿入（`Update` / `PreLateUpdate` 等）で、毎フレーム決まった時点に処理をフラッシュできる。これにより並列アクセスが多くてもキュー経由で確実に順序が保てる。
  - `MonoBehaviour.Update` に依存するコミュニティ実装（UnityMainThreadDispatcher）も一般的だが、本 spec はシェル側のシーンオブジェクトに依存せず `PlayerLoop` 挿入で自己完結させる方がクリーン。
- **Implications**:
  - メインスレッド配信キューは **`PlayerLoopSystem` の `PreUpdate` フェーズに挿入する `IpcDispatchStep`** として実装する。R-4 への回答：`PreUpdate` 段階で受信キューをフラッシュすることで、UI 側は同フレームの `Update` で最新値を読める。メイン出力側は描画前にコマンドが反映されるため「描画と入力が 1 フレームずれる」事態を防げる。
  - `state` メッセージの coalesce（最新値上書き）と `event` の FIFO 保持を、`MainThreadDispatchQueue` という内部コンポーネント（トピック別スロット辞書 + FIFO キュー）で併置する。

### Topic 3: JSON シリアライゼーションの選定

- **Context**: D-5 により JSON / WebSocket テキストフレームに確定。Unity 環境で前方互換性（未知フィールド無視、Req 3.7）を維持しつつ、エンベロープ + 任意ペイロード（Dictionary 的構造）を扱える方式が必要。
- **Sources Consulted**:
  - [A Unity Developer's Guide to Mastering JSON](https://medium.com/@rohan5210work/a-unity-developers-guide-to-mastering-json-from-jsonutility-to-newtonsoft-102de5ef9c11)
  - [Json performance comparison - Unity Discussions](https://discussions.unity.com/t/json-performance-comparison/844318)
  - [System.Text.Json vs Newtonsoft.Json in 2026](https://wirefuture.com/post/system-text-json-vs-newtonsoft-json-in-2026-still-relevant)
  - [Benchmarking System.Text.Json vs Newtonsoft.Json in .NET 10](https://jkrussell.dev/blog/system-text-json-vs-newtonsoft-json-benchmark/)
- **Findings**:
  - `UnityEngine.JsonUtility` は Dictionary 非対応、`[Serializable]` クラスのみ対応、未知フィールドは復元されないが壊れない（無視される）性質はある一方で、ペイロード部分を動的型として扱う用途には向かない。
  - `Newtonsoft.Json`（`com.unity.nuget.newtonsoft-json`）は動的型・Dictionary・未知フィールド対応が完璧だが、メモリアロケーションが大きい。
  - `System.Text.Json` は .NET Standard 2.1 以降で利用可能、`JsonElement` による動的アクセス、未知フィールドのスキップ、パフォーマンス・メモリ効率とも良好（2–3 倍高速、メモリ 40–60% 削減）。
- **Implications**:
  - 本 spec は **`System.Text.Json`** を一次採用する。理由：(1) Req 3.7 の「未知フィールドを無視して処理継続」を既定挙動で満たす、(2) エンベロープ外層（`kind` / `topic` / `correlationId` 等）は厳密型、ペイロードは `JsonElement` で保持し上位 spec の任意型にマップ可能、(3) サイズ制限（Req 3.9–3.10、1MB）を事前にバイト長で検査できる。
  - 依存追加が必要な場合は UPM `com.unity.nuget.newtonsoft-json` で代替可能な抽象層（`IMessageCodec`）を挟み、実装差し替えに備える。

### Topic 4: デフォルトポート番号の選定（R-1 解消）

- **Context**: D-6 に従い設定ファイルで上書き可能だが、未指定時のデフォルトを確定する必要がある。
- **Sources Consulted**:
  - [RFC 6455 - The WebSocket Protocol](https://datatracker.ietf.org/doc/html/rfc6455)
  - [List of TCP and UDP port numbers - Wikipedia](https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers)
  - [IANA Port Numbers](https://jachguate.github.io/indydocs/html/IANAPortNumbers.html)
- **Findings**:
  - Dynamic / Private 範囲（49152–65535）から選定することが RFC / IANA の推奨。既知 VTuber 系ツール（VSeeFace: 39540 VMCProtocol 等、VMC Protocol: 39539–39543）との衝突を避ける必要がある。
  - OBS WebSocket のデフォルト（4455）やローカル開発で一般的なポート（3000, 8080, 8000）との衝突も避けたい。
  - UCAPI / SceneViewStyleCameraController 等の採用パッケージはいずれも OSC を使い、本 IPC 経路と別物である。
- **Implications**:
  - **デフォルトポート: `61874`**（49152–65535 の Dynamic/Private 範囲内、VMC / OBS / 汎用開発ポートと衝突しない値を選定）。ゴロ合わせ: "V-Tuba" の数字化に近い範囲。設定ファイルで容易に変更可能。
  - `127.0.0.1:61874` を LocalHost デフォルトエンドポイントとする。

### Topic 5: 再接続バックオフ戦略（R-3 解消）

- **Context**: Req 5.2 に従いクライアント側は接続失敗・接続断時に再接続を試行する。初期間隔・倍率・上限・試行回数を確定する必要がある。
- **Sources Consulted**:
  - [Developing a dependable client-side WebSocket solution for Unity - Ably](https://ably.com/topic/websockets-unity)
  - [RFC 6455 - The WebSocket Protocol](https://datatracker.ietf.org/doc/html/rfc6455)
- **Findings**:
  - LocalHost 環境では遅延はミリ秒オーダーのため、バックオフ初期値を大きく取る必要はない。
  - Wave 1 のケースは主に「UI 側起動時にサーバがまだ待受前」のレース。1 秒以内に複数回試行する形が実用的。
  - 将来の LAN / WebUI では回線断を想定し、上限間隔を 5–10 秒にする標準的パターンが推奨される。
- **Implications**:
  - **指数バックオフ**: 初期間隔 250 ms、倍率 2.0、上限間隔 5 秒、最大試行回数 20 回（合計約 90 秒以内に諦める）をデフォルトとする。これらも設定ファイルで上書き可能。
  - 試行上限超過時は `ConnectionState.PermanentlyDisconnected` へ遷移し、Req 5.5 に従い上位へ通知する。

### Topic 6: イベントキュー上限値（R-2 解消）

- **Context**: Req 9.5「event 滞留が上限を超えた場合に警告ログを出力」の具体値を確定する必要がある。
- **Findings**:
  - 本 spec の想定ワークロード：カメラ切替・Light 追加・プリセット適用などの event は 1 配信中数十〜数百オーダー。スライダー操作等の高頻度は `state` 側で coalesce される。
  - 1 トピックあたり同時滞留 1000 件を超えるのは実質「ハンドラがメインスレッドで詰まっている」異常状態。
- **Implications**:
  - **event キュー警告しきい値: トピックあたり 1000 件**、合計 10000 件。超過時は警告ログを出し、Req 9.5 に従い破棄はせず保持を続ける（ただしメモリ保護のため、さらに 5 倍（50000 件合計）で hard-drop ポリシーに移行する拡張点を残す）。

### Topic 7: 設定ファイルのフォーマット・配置（R-5 解消）

- **Context**: D-6 に従い設定ファイルで構成可能にするが、フォーマット・配置を確定する必要がある。
- **Findings**:
  - Unity プロジェクトでの設定永続化パターン：(a) `ScriptableObject` アセット、(b) `Resources/` + JSON、(c) `StreamingAssets/` + JSON、(d) `%AppData%` 等の外部ディレクトリ。
  - 本 spec は「配信現場で運用者がポートを変える」ユースケースを想定するため、**ビルド後でも編集可能な外部設定**が要件を満たす。
- **Implications**:
  - **設定ファイル形式**: JSON。
  - **配置**: (1) 既定値は `ScriptableObject`（`CoreIpcConfigAsset`）として UPM パッケージに同梱、(2) 実行時は `StreamingAssets/core-ipc-config.json` が存在すればそちらを優先、(3) `%AppData%/VTuberSystemBase/core-ipc-config.json` がさらに優先（運用者の per-machine 上書き用）。
  - 同様の設定管理は他 spec でも再利用される可能性があるが、本 spec 単体で完結する範囲に留める。steering 側で全体方針が確立されれば移行可能な抽象化を維持する。

### Topic 8: スキーマ versioning 戦略（R-6 解消）

- **Context**: Req 3.7 の「未知フィールド無視」は決定済みだが、互換性を壊す変更時の対応が未設計。
- **Findings**:
  - 一般的アプローチ：(a) エンベロープに `protocolVersion`（例: `"1.0"`）を含める、(b) Semantic Versioning に従いマイナーは後方互換、メジャーは非互換。
  - 本 spec は初版であり、当面は `1.0` のみ扱う。
- **Implications**:
  - エンベロープに `protocolVersion: string` フィールドを含める（既定値 `"1.0"`）。
  - 受信時、メジャー版が異なる（例: `2.x`）メッセージを受信した場合は**破棄 + 診断ログ**。マイナー版差異は既定の「未知フィールド無視」で吸収される。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| Hexagonal (Ports & Adapters) | コアドメイン（エンベロープ、Pub/Sub、Request/Response）を port 定義とし、トランスポート・シリアライザ・ディスパッチャを adapter として実装 | 抽象インタフェース境界が明確／具体実装差し替え容易／テスト可能性が高い | adapter 層の記述量増／Unity 初心者には理解コスト | D-2/D-5 の「具体実装 1 本」と Req 6.2/6.3 の「将来の別実装受け入れ」を両立できる |
| Layered (Transport → Codec → Dispatcher → API) | 単純な層状分離、各層が下位層のインタフェースのみに依存 | 実装がストレート／学習コスト低 | トランスポート差し替え時に上位層への漏れが発生しやすい | Req 1.2 の「具体トランスポートへの型依存排除」を満たすには依存方向の厳密な enforcement が必要 |
| Actor / Message-passing | 各コンポーネントを actor 化し、メッセージ受け取りで処理 | 並行性モデルが明確 | Unity メインスレッド集約モデル（D-3）と噛み合わない／actor 層の複雑度増 | 却下 |
| Event Bus 単一 | 単一 EventBus が全メッセージを仲介 | 極単純 | Pub/Sub と Request/Response のセマンティクス差を埋める追加機構が必要／ロール（server/client）の抽象化困難 | 却下 |

**Selected**: Hexagonal（Ports & Adapters）。

**Rationale**:
- Req 1.1–1.6 の抽象インタフェース要件と Req 2.1–2.2（具体実装を 1 つの adapter として提供）が port/adapter 分離と 1:1 に対応する。
- Req 6.2–6.3（将来トランスポート追加時に上位コード変更不要）が adapter 追加のみで満たせる。
- Req 8.1（自己ループによる単体検証）は `InMemoryTransportAdapter`（テスト用 adapter）で実現可能。

## Design Decisions

### Decision: トランスポートアダプタは「サーバ側 TcpListener + 手書き RFC 6455」と「クライアント側 ClientWebSocket」で構成する

- **Context**: Unity の `HttpListener` が WebSocket 拡張を未実装のため、サーバ側で `System.Net.WebSockets` API をそのまま使えない。
- **Alternatives Considered**:
  1. Fleck（MIT、純 C#）を組み込む — 外部依存が増える／UPM 配布での NuGet 統合が煩雑。
  2. websocket-sharp を組み込む — Unity 実績は豊富だが更新頻度が低く、長期メンテナンスリスク。
  3. `TcpListener` + 手書き RFC 6455 ハンドシェイク — 依存ゼロ。必要な機能は最小（テキストフレーム・fragmentation・ping/pong/close）なので実装量も限定的。
- **Selected Approach**: **(3) 自前実装**。`TransportAdapter.WebSocket.Server` 内で `TcpListener` を起動し、HTTP アップグレード要求を検出 → Sec-WebSocket-Accept を計算 → フレーマを自前で実装。クライアント側は `System.Net.WebSockets.ClientWebSocket` を利用。
- **Rationale**: 本 spec のスコープは「WebSocket 最小機能 + LocalHost ループバック」であり、外部ライブラリが提供する高度機能（permessage-deflate、サブプロトコル、認証等）は不要。UPM パッケージとして配布する際も third-party 依存が増えない方がフォーク・再配布が容易。
- **Trade-offs**:
  - Pros: ゼロ外部依存／実装範囲が明示的／Unity の HttpListener 制約を回避。
  - Cons: RFC 6455 の実装責任を自前で負う／将来 permessage-deflate が必要になった時点で再実装コストが発生。
- **Follow-up**: 実装時にセキュリティ観点で (i) 最大フレーム長チェック、(ii) UTF-8 妥当性検証、(iii) close ハンドシェイクの timeout（5 秒）を確実に実装する。

### Decision: メインスレッド配信は `PlayerLoopSystem.PreUpdate` へのカスタムステップ挿入で実現する

- **Context**: D-3 により受信コールバックは常に Unity メインスレッド。I/O はワーカースレッドで行う。
- **Alternatives Considered**:
  1. `MonoBehaviour.Update` に乗せる — シーンに常駐 GameObject が必要、PlayMode 依存、ドメインリロード影響を受けやすい。
  2. `UnitySynchronizationContext` への `Post` — 既定で `Update` タイミングで実行。優先順位制御が難しい。
  3. `PlayerLoopSystem` に独自ステップを挿入 — フェーズ（`Initialization` / `EarlyUpdate` / `PreUpdate` / `Update` / `PreLateUpdate` / ...）の任意位置にフックできる。シーン非依存。
- **Selected Approach**: **(3) PlayerLoop 挿入**。`IpcDispatchStep` を `PreUpdate` フェーズに挿入し、各フレームの `Update` 直前に受信キューをフラッシュ。
- **Rationale**: シーンオブジェクト不要、Edit モード自動除外（`PlayerLoop` は PlayMode 中のみ稼働）、Unity の標準パターンに沿う。`PreUpdate` 配置により同フレームの `Update` で最新値を読める。
- **Trade-offs**:
  - Pros: シーン独立／PlayMode 自動連動／決定論的タイミング。
  - Cons: `PlayerLoop` API 使用時は挿入/削除を対称に管理する必要がある（漏らすと PlayMode 繰り返しで多重登録）。
- **Follow-up**: `RuntimeInitializeOnLoadMethod` で挿入、`PlayModeStateChange.ExitingPlayMode` と `Application.quitting` で削除、Editor とスタンドアロンで挙動を確認。

### Decision: エンベロープは厳密型外層 + `JsonElement` ペイロードのハイブリッド

- **Context**: Req 3.1 のエンベロープ定義と Req 3.7 の前方互換性（未知フィールド無視）を両立する必要がある。上位 spec はペイロード部分を任意の型へマップする。
- **Alternatives Considered**:
  1. 全て `Dictionary<string, object>` で保持 — 型安全性が完全に失われる（design-principles §1 違反）。
  2. エンベロープ全体を固定型 + ペイロードは文字列として保持 — デシリアライズが 2 段階になり効率が悪い。
  3. エンベロープ外層は固定型（`MessageEnvelope` struct）、ペイロードは `System.Text.Json.JsonElement` または生 `ReadOnlyMemory<byte>` として保持し、上位 spec が必要なタイミングで任意型へマップ。
- **Selected Approach**: **(3) ハイブリッド**。外層（`protocolVersion` / `kind` / `topic` / `correlationId` / `timestamp` / `payload`）は厳密型。`payload` は `JsonElement`（`System.Text.Json`）として保持。`TrySerialize<T>` / `TryDeserialize<T>` のヘルパで型安全に変換可能。
- **Rationale**: 型安全性を最小限犠牲にしつつ、未知フィールドは `System.Text.Json` の既定挙動で読み飛ばされ、上位 spec は自身のペイロード型（generic 型パラメータ）を持ち込める。
- **Trade-offs**: ペイロード型検証は上位 spec 側の責務となる（本 spec は envelope の妥当性のみ検証）。Req 4 の「ハンドラ登録時に型指定」を導入することで上位 spec の実装負担を軽減する。
- **Follow-up**: `IMessageCodec` インタフェースで将来 MessagePack 等へ切替可能な抽象を残す。

### Decision: 送信 API は `PublishState` / `PublishEvent` / `Request` の 3 系統独立

- **Context**: D-10 により送信 API を意図別に分離する。
- **Alternatives Considered**:
  1. 単一 `Publish(topic, payload, kind)` API — 呼び出し側が `kind` を引数で間違えるリスク。
  2. 3 系統独立の `PublishState(topic, payload)` / `PublishEvent(topic, payload)` / `Request<TReq, TRes>(topic, payload, ct)` — 呼び出し側コードで意図が明示。
- **Selected Approach**: **(2) 3 系統独立**。API シグネチャで `kind` が暗黙に決定されるため、誤用が構造的に防がれる。
- **Rationale**: design-principles §4「Interface Segregation: Minimal, focused interfaces」に整合。D-10 の直接実装。
- **Trade-offs**: API 表面が 3 つに増えるが、可読性・誤用防止効果がはるかに大きい。
- **Follow-up**: 購読側は `SubscribeState<T>` / `SubscribeEvent<T>` / `RegisterRequestHandler<TReq, TRes>` の対称形で提供する。

### Decision: 接続マネージャは PlayMode 依存の単一 Singleton（`CoreIpcRuntime`）

- **Context**: D-9 の「PlayMode 開始〜停止の区間のみ常駐、Edit モードでは常駐しない」を構造的に満たす必要がある。
- **Alternatives Considered**:
  1. `MonoBehaviour` ベースの常駐オブジェクト — シーン依存、破棄タイミング制御が煩雑。
  2. `static` クラス + `RuntimeInitializeOnLoadMethod` — シーン独立だが生存期間の制御が難しい。
  3. 純 C# Singleton + `RuntimeInitializeOnLoadMethod`（`BeforeSceneLoad`）での起動 + `Application.quitting` / `EditorApplication.playModeStateChanged` での停止。
- **Selected Approach**: **(3) C# Singleton + PlayerLoop 管理**。`CoreIpcRuntime` は `IDisposable` を実装し、PlayMode 開始で `Initialize`、PlayMode 終了 / アプリ終了で `Dispose`。
- **Rationale**: シーン非依存、Edit モード非起動、ドメインリロード自動対応（`RuntimeInitializeOnLoadMethod` は PlayMode 開始時の最新 Domain で毎回実行される）。
- **Trade-offs**: Singleton パターンは一般にテストしにくいが、`ICoreIpcRuntime` インタフェースを介した DI でテストダブル差し替えを許容する。
- **Follow-up**: Req 8.6 のモック実装受け入れと整合するよう、Singleton 参照は `CoreIpcRuntime.Current`（`ICoreIpcRuntime`）とし、テスト時に `CoreIpcRuntime.OverrideForTesting(...)` で差し替え可能にする。

## Risks & Mitigations

- **R-1（残）**: RFC 6455 自前実装の品質リスク — Autobahn TestSuite 等の互換性テストを Wave 1 の完了判定に含める。最小機能（テキスト・fragmentation・control frame）に実装範囲を絞り、拡張機能は実装しない。
- **R-2（新）**: `PlayerLoop` 挿入/削除の対称性破綻 — `CoreIpcRuntime.Dispose` で必ず削除、削除失敗時は警告ログ。PlayMode 繰り返しテスト（5 回以上）をテストスイートに含める。
- **R-3（新）**: `System.Text.Json` の Unity 互換性 — .NET Standard 2.1 プロファイルで利用可能。万一問題が出た場合のフォールバックとして `com.unity.nuget.newtonsoft-json` 経由の `INewtonsoftJsonCodec` を `IMessageCodec` の代替実装として用意可能な抽象を維持。
- **R-4（既）**: デフォルトポート 61874 の衝突 — 設定ファイルでの上書きを第一推奨とし、起動時にバインド失敗した場合は明示エラー（Req 2.6）で通知。
- **R-5（新）**: 将来 LAN / WebUI クライアント追加時のセキュリティ欠如 — Req 6.6 の通り本フェーズでは認証・暗号化を実装しないが、インタフェースに `IAuthenticationHandler` フック点（未使用）を残すことで将来の追加を非破壊にする（インタフェース上の no-op スロット）。

## References

- [RFC 6455 - The WebSocket Protocol](https://datatracker.ietf.org/doc/html/rfc6455) — トランスポート実装の規範。
- [Unity Manual - Awaitable (Unity 6)](https://docs.unity3d.com/6000.3/Documentation/Manual/async-awaitable-continuations.html) — メインスレッドディスパッチの公式情報。
- [UnitySynchronizationContext.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs) — Unity の Sync Context 実装ソース。
- [HttpListener and Websockets - Unity Discussions](https://discussions.unity.com/t/httplistener-and-websockets/820948) — Unity での HttpListener WebSocket 未実装問題。
- [System.Text.Json vs Newtonsoft.Json in 2026](https://wirefuture.com/post/system-text-json-vs-newtonsoft-json-in-2026-still-relevant) — JSON 実装比較。
- [System.Text.Json vs Newtonsoft.Json Benchmark in .NET 10](https://jkrussell.dev/blog/system-text-json-vs-newtonsoft-json-benchmark/) — パフォーマンスベンチマーク。
- [A Unity Developer's Guide to Mastering JSON](https://medium.com/@rohan5210work/a-unity-developers-guide-to-mastering-json-from-jsonutility-to-newtonsoft-102de5ef9c11) — Unity における JSON 選定ガイド。
- [GitHub - statianzo/Fleck](https://github.com/statianzo/Fleck) — 代替案として評価した WebSocket サーバ実装。
- [GitHub - endel/NativeWebSocket](https://github.com/endel/NativeWebSocket) — WebSocket クライアント実装参考。
- [List of TCP and UDP port numbers - Wikipedia](https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers) — ポート衝突調査。
- [Unity Main Thread Dispatcher - gustavopsantos](https://github.com/gustavopsantos/UnityMainThreadDispatcher) — コミュニティ実装の比較参考。
