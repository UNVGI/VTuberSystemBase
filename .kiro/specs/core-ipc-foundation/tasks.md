# Implementation Plan

本 spec は **Hexagonal（Ports & Adapters）** パターンに基づき、純 C# コアを内側に、Unity 依存と I/O 依存のアダプタを外周に配置する構成を持つ。タスクは TDD 原則に従い、各コンポーネントの単体テスト（失敗する状態から着手）→ 実装 → 統合 → PlayMode 検証の順で進める。`(P)` マーカーは境界が重ならない独立タスクに限定して付与する。

---

## 1. 基盤セットアップ（UPM パッケージ骨組み、asmdef 配線、テストインフラ）

- [ ] 1.1 UPM パッケージと asmdef 階層の作成
  - `Packages/com.vtuber-system-base.core-ipc-foundation/` を作成し、`package.json`（name / version / unity / description）を配置する
  - `Runtime/Abstractions/VTuberSystemBase.CoreIpc.Abstractions.asmdef` を追加し、他アセンブリに一切依存しない純インタフェース層として宣言する
  - `Runtime/Core/VTuberSystemBase.CoreIpc.Core.asmdef` を追加し、`Abstractions` のみへの参照を宣言する
  - `Editor/VTuberSystemBase.CoreIpc.Editor.asmdef` を `includePlatforms: Editor` で追加し、`Abstractions` + `Core` を参照する
  - `Tests/Runtime/` と `Tests/Editor/` に Unity Test Framework 参照の asmdef を配置する
  - 観測可能な完了条件: Unity Editor でパッケージが認識され、各 asmdef が期待した参照方向（Adapters→Domain←Upper Specs）で依存解決され、Rider/VS のソリューションで 4 つの C# プロジェクトが生成される
  - _Requirements: 1.6, 1.2_

- [ ] 1.2 CoreIpcOptions と CoreIpcError の抽象定義
  - `CoreIpcOptions` レコードをホスト・ポート・再接続パラメータ・メッセージサイズ上限・ログレベル等の既定値付きで定義する
  - `CoreIpcError` discriminated union を定義し、`NotConnected` / `SizeLimitExceeded` / `InvalidTopic` / `InvalidEnvelope` / `RequestTimeout` / `PortInUse` / `ProtocolVersionMismatch` / `TransportFailure` / `HandlerException` の各バリアントを提供する
  - `IpcResult` と `IpcResult<T>` を同期結果型として追加し、`Ok` / `Fail` のファクトリを提供する
  - 観測可能な完了条件: 既定値でインスタンス化した `CoreIpcOptions` が Host=`127.0.0.1`, Port=`61874`, DefaultRequestTimeout=`5s`, MaxMessageSizeBytes=`1_048_576` を返す単体テストが通る
  - _Requirements: 2.7, 3.9, 6.1, 7.4_

- [ ] 1.3 MessageEnvelope と MessageKind / ConnectionState 型定義
  - `MessageEnvelope` を `protocolVersion` / `kind` / `topic` / `correlationId` / `timestampUnixMs` / `payload(JsonElement)` を持つ record struct として定義する
  - `MessageKind` enum（`State` / `Event` / `Request` / `Response`）を定義する
  - `ConnectionState` enum（`Disconnected` / `Connecting` / `Connected` / `Reconnecting` / `PermanentlyDisconnected`）を定義する
  - `RuntimeState` enum（`NotInitialized` / `Initializing` / `Running` / `ShuttingDown` / `Disposed`）を定義する
  - 観測可能な完了条件: 各 enum と record struct が `Abstractions` asmdef から公開され、`Core` asmdef から参照可能であることがビルドで確認できる
  - _Requirements: 3.1, 3.6, 3.8_

- [ ] 1.4 ICoreIpcBus / ICoreIpcRuntime / ITransportAdapter / IMessageCodec の抽象インタフェース定義
  - `ICoreIpcBus` に `PublishState` / `PublishEvent` / `RequestAsync` / `SubscribeState` / `SubscribeEvent` / `RegisterRequestHandler` / `Diagnostics` を宣言する
  - `ICoreIpcRuntime` に `State` / `Bus` / `Options` / `InitializeAsync` を宣言し、`IDisposable` を継承する
  - `ITransportAdapter` に `StartServerAsync` / `ConnectClientAsync` / `ClientConnected` / `ClientDisconnected` を宣言し、`IClientConnection` に `SendAsync` / `ReceiveAsync` を宣言する
  - `IMessageCodec` に `Encode` / `Decode` を宣言する
  - `IConnectionDiagnostics` に `CurrentState` / `ReconnectAttemptCount` / `PendingRequestCount` / `StateSlotCount` / `EventQueueCount` / `ConnectedClientCount` / `ConnectionStateChanged` / `TakeSnapshot` を宣言する
  - `ISubscriptionToken` / `RequestOptions` / `ServerBindOptions` / `ClientBindOptions` / `DiagnosticsSnapshot` / `IAuthenticationHandler`（no-op スロット）を追加する
  - 観測可能な完了条件: 全抽象インタフェースが `Abstractions` asmdef に存在し、具体型への前方参照が皆無であることを依存解析ツール（または asmdef の参照確認）で確認できる
  - _Requirements: 1.1, 1.2, 1.6, 5.1, 5.3, 5.6, 6.2, 6.6, 7.5_

- [ ] 1.5 テストインフラ整備と共通テストユーティリティ
  - `Tests/Runtime/` と `Tests/Editor/` に NUnit テストランナーが走る最小テストを配置し、CI で実行可能な状態にする
  - テスト間共有の `TestMainThreadPump`（PlayerLoop を使わずに Flush を駆動するヘルパ）と `FakeClock` を `Tests/Runtime/TestSupport/` に置く
  - 観測可能な完了条件: `Test Runner` で Runtime / Editor 両カテゴリの空テストが緑になり、共通ユーティリティが他のテストから参照可能
  - _Requirements: 8.2_

---

## 2. コアドメイン実装（エンベロープ、Codec、配信キュー、相関、購読）

- [ ] 2.1 (P) SystemTextJsonCodec の実装とラウンドトリップテスト
  - `IMessageCodec` を `System.Text.Json` で実装し、`MessageEnvelope` の外層を厳密型、`payload` を `JsonElement` として扱う
  - 未知フィールドを読み飛ばすように `JsonSerializerOptions` を構成し、`protocolVersion` メジャー不一致（`2.x` 以上）は `ProtocolVersionMismatch` エラーに変換する
  - 不正 JSON / スキーマ不一致は `InvalidEnvelope` エラーに変換し、呼び出し側へ結果を返す（例外伝搬しない）
  - 受信バイト長が `MaxMessageSizeBytes`（1 MB）を超える場合は `SizeLimitExceeded` を返す
  - 単体テストで state/event/request/response 各 kind のエンコード→デコードラウンドトリップ、未知フィールド付き入力の後方互換、1 MB 超過入力の拒否、メジャー不一致の拒否を検証する
  - 観測可能な完了条件: 7 本以上の Codec 単体テストが緑になり、全 kind のラウンドトリップが入力と等価な出力を返す
  - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.7, 3.10_
  - _Boundary: SystemTextJsonCodec_

- [ ] 2.2 (P) MainThreadDispatchQueue の実装（state coalesce + event FIFO）
  - `ConcurrentDictionary<string, MessageEnvelope>` を topic 単位の state スロットとして保持し、同一 topic の state は原子的に上書きする
  - `Channel<MessageEnvelope>` を event FIFO キューとして保持し、enqueue 順で Flush に供する
  - `Flush()` メソッドを単一メインスレッドアクセス前提で実装し、state スナップショット→event ドレイン→購読ハンドラ呼び出しの順で配信する
  - 各ハンドラ呼び出しは try/catch で隔離し、例外は `HandlerException` としてログ出力しつつループを継続する
  - topic あたり event 滞留が `EventQueueWarningThresholdPerTopic`（既定 1000）を超えたら警告ログを出力する（破棄はしない）
  - 単体テストで同一 topic state 10 件 → Flush 1 回で最新 1 件のみ配信、event 1000 件の FIFO 順序保存、ハンドラ例外時の後続配信継続を検証する
  - 観測可能な完了条件: coalesce / FIFO / 警告しきい値 / 例外隔離の 4 系統のテストが全て緑
  - _Requirements: 1.4, 1.7, 9.1, 9.2, 9.4, 9.5_
  - _Boundary: MainThreadDispatchQueue_

- [ ] 2.3 (P) RequestCorrelationRegistry の実装（相関 ID・TCS・タイムアウト）
  - `ConcurrentDictionary<string, PendingRequest>` に GUID 相関 ID で TCS を登録し、`MatchResponse` で対応する TCS をメインスレッドディスパッチキュー経由で完了させる
  - `System.Threading.Timer` で Request 単位のタイムアウトを管理し、デフォルト 5 秒、`RequestOptions.Timeout` で上書き可能とする
  - タイムアウト発火時は TCS を `RequestTimeout` で完了させ、辞書から pending を除去する
  - Shutdown 時は全 pending を `NotConnected` で完了させ、`CancellationToken` 登録も漏れなく解除する
  - 単体テストで、5 秒無応答でのタイムアウト、明示タイムアウト上書き、応答マッチング、Shutdown 時の全 pending 解決を検証する
  - 観測可能な完了条件: 4 本の単体テストが緑で、100 並行 Request のうち応答/タイムアウト/キャンセルの各分岐が漏れなく解決されることを確認
  - _Requirements: 1.5, 3.6, 9.3_
  - _Boundary: RequestCorrelationRegistry_

- [ ] 2.4 (P) TopicSubscriptionRegistry と SubscriptionToken の実装
  - `topic` × `kind` 単位でハンドラ（delegate + payload Type）を登録・解除する辞書を実装する
  - `ISubscriptionToken` の `Dispose` で登録解除、多重 Dispose は no-op とする
  - `MainThreadDispatchQueue` が Flush 時に参照する lookup API（`TryGetHandlers(topic, kind)`）を提供する
  - 単体テストで登録→配信→解除→配信無しのライフサイクル、同一 topic への複数ハンドラ並列登録、Dispose 冪等性を検証する
  - 観測可能な完了条件: 購読登録・解除の 3 本のテストが緑で、解除後に当該 topic のハンドラが呼ばれない
  - _Requirements: 1.1, 1.4_
  - _Boundary: TopicSubscriptionRegistry_

- [ ] 2.5 CoreIpcBus の実装（送信プリチェック、購読委譲、Request 発行）
  - `PublishState` / `PublishEvent` でエンベロープを構築し、サイズ 1 MB 検査と空 topic 検証（`InvalidTopic`）を行った後、`IMessageCodec.Encode` → `ITransportAdapter` の送信キューへ投入する
  - 接続未確立時は `IpcResult.Fail(NotConnected())` を返し、例外を投げずクラッシュを防ぐ
  - `RequestAsync` では `RequestCorrelationRegistry.AllocateCorrelationId` で ID を採番し、TCS を登録して応答待ちタスクを返す
  - `SubscribeState` / `SubscribeEvent` / `RegisterRequestHandler` を `TopicSubscriptionRegistry` へ委譲する
  - 単体テストで、接続断時の `NotConnected` 返却、サイズ超過拒否、空 topic 拒否、Request の相関付き送信を検証する
  - 観測可能な完了条件: 上記 4 系統のテストが緑で、`CoreIpcBus` が Unity API を直接呼ばない（純 C# で単体テスト可能）
  - _Requirements: 1.1, 1.3, 3.9, 5.4, 9.6_
  - _Boundary: CoreIpcBus_

---

## 3. 接続管理レイヤ（状態機械、バックオフ、セッション管理）

- [ ] 3.1 (P) ConnectionStateMachine の実装
  - 5 状態（`Disconnected` / `Connecting` / `Connected` / `Reconnecting` / `PermanentlyDisconnected`）の遷移を単一エントリで扱う
  - 遷移ごとに `ConnectionStateChanged(previous, current)` イベントを発火する
  - 不正遷移（例: `Disposed` 状態からの `Connecting`）は例外ではなくログ警告で無視する
  - Shutdown 経由の `Disconnected` 遷移は再接続を誘発しないフラグ付きで実行する
  - 単体テストで、全遷移パスと Shutdown フラグ時の再接続抑止を検証する
  - 観測可能な完了条件: 状態遷移マトリクステスト（少なくとも 8 遷移パス）が緑
  - _Requirements: 5.1, 5.3, 5.5, 5.6, 5.8_
  - _Boundary: ConnectionStateMachine_

- [ ] 3.2 (P) ReconnectBackoff の実装
  - 初期遅延・倍率・上限遅延・最大試行回数をコンストラクタで受け取る
  - `NextDelay()` を呼ぶごとに `initial * multiplier^n` を計算し `maxDelay` で cap する
  - `ExceededMaxAttempts` が `true` になった後は `NextDelay()` を呼ばず上位が `PermanentlyDisconnected` 遷移を選ぶ契約とする
  - `Reset()` で試行回数を 0 に戻す
  - 単体テストで 250ms → 500ms → 1s → 2s → 4s → 5s(cap) → 5s × n の系列、20 回で `ExceededMaxAttempts=true`、`Reset` 後の再計算を検証する
  - 観測可能な完了条件: バックオフ系列テストが緑で、設計値どおりの遅延列が返る
  - _Requirements: 5.2, 5.5_
  - _Boundary: ReconnectBackoff_

- [ ] 3.3 ClientSessionManager の実装（接続・切断検知・再接続駆動）
  - `ITransportAdapter.ConnectClientAsync` を呼んで `IClientConnection` を確立し、`ConnectionStateMachine` を `Connected` に遷移させる
  - `IClientConnection.ReceiveAsync` のループをワーカー `Task` として回し、切断検知で `Reconnecting` に遷移して `ReconnectBackoff` に従い再試行する
  - 再試行が上限超過で `PermanentlyDisconnected` へ遷移し、以後自動再試行を停止する（Req 5.5）
  - Shutdown 要求時は再試行せず `Disconnected` にクリーン遷移する（Req 5.8）
  - 接続断中の `Publish*` 呼び出しに対しては `CoreIpcBus` 経由で `NotConnected` を返す（直接の責務は `CoreIpcBus`）
  - 単体テストを `ITransportAdapter` のモックで作成し、接続成功→切断→再接続成功のシーケンス、上限超過での `PermanentlyDisconnected`、Shutdown 時の再試行抑止を検証する
  - 観測可能な完了条件: モックトランスポートで 4 シナリオ（成功 / 切断リカバリ / 永久切断 / シャットダウン）が緑
  - _Requirements: 2.4, 5.2, 5.4, 5.5, 5.8_
  - _Boundary: ClientSessionManager_

---

## 4. WebSocket トランスポートアダプタ実装（RFC 6455）

- [ ] 4.1 (P) WebSocketFrameReader / WebSocketFrameWriter の実装
  - RFC 6455 のフレームヘッダを解析し、`FIN` / `opcode` / `mask` / `payload length` / `masking key` を取り出す
  - サポートオペコード: Text (0x1), Close (0x8), Ping (0x9), Pong (0xA), Continuation (0x0)
  - クライアント→サーバ方向の masking 必須検証と、サーバ→クライアント方向の非マスク送信を行う
  - フラグメンテーション対応、累積ペイロード 1 MB 超過は close code 1009 で切断する
  - UTF-8 妥当性検証（Text フレーム）、不正時は close code 1007 で切断
  - Editor テストで、仕様適合の各フレーム形状（mask 有/無、fragment、ping/pong、close）の読書きラウンドトリップを検証する
  - 観測可能な完了条件: 10 本以上のフレーム Codec テストが緑で、RFC 6455 §5 の主要ケースを網羅する
  - _Requirements: 2.1, 2.5, 3.10_
  - _Boundary: WebSocketFrameReader, WebSocketFrameWriter_

- [ ] 4.2 HandshakeProcessor の実装（Sec-WebSocket-Accept 計算）
  - HTTP `GET / HTTP/1.1` + `Upgrade: websocket` + `Sec-WebSocket-Key` ヘッダを解析する
  - `Sec-WebSocket-Key` + GUID `258EAFA5-E914-47DA-95CA-C5AB0DC85B11` を SHA-1 → Base64 で `Sec-WebSocket-Accept` を生成する
  - 101 Switching Protocols レスポンスを構築して返す
  - 不正リクエスト（メソッド違反、必須ヘッダ欠落）は 400 Bad Request で応答する
  - 単体テストで、RFC 6455 §4.2.2 の例（key=`dGhlIHNhbXBsZSBub25jZQ==` → accept=`s3pPLMBiTxaQ9kYGzzhZRbK+xOo=`）を含む 3 本以上のケースを検証する
  - 観測可能な完了条件: RFC 例のベクタが正確に一致する
  - _Requirements: 2.1, 2.3_

- [ ] 4.3 WebSocketServer の実装（TcpListener ベースの自前実装）
  - `System.Net.Sockets.TcpListener` で待受を開始し、`SocketOptionName.ReuseAddress` を有効にして PlayMode 再起動でのバインド失敗を抑止する
  - ポート占有検出時は `SocketException` を `CoreIpcError.PortInUse` に変換して起動失敗を上位へ伝搬する（例外非伝搬、描画ループに影響させない）
  - 接続受理ごとに独立 `Task` を起動し、`HandshakeProcessor` でアップグレードを完了させてから `WebSocketFrameReader/Writer` で送受信を回す
  - `ClientConnected` / `ClientDisconnected` イベントを発火し、複数クライアント同時接続をサポート（接続上限 16、設定で変更可能）
  - Ping 30 秒アイドル送信、Pong 60 秒無応答でタイムアウト切断、Close 5 秒タイムアウトの制御を実装する
  - 全例外を try/catch で隔離し、メイン出力の描画継続を阻害しない契約を保つ（Req 5.7）
  - 観測可能な完了条件: 単体プロセスでサーバ起動→ローカルクライアント接続→テキストフレーム送受信→切断が確認でき、ポート占有時に `PortInUse` が上位へ返る
  - _Requirements: 2.1, 2.3, 2.5, 2.6, 2.8, 5.7_
  - _Boundary: WebSocketServer_

- [ ] 4.4 WebSocketClient の実装（ClientWebSocket ラッパ）
  - `System.Net.WebSockets.ClientWebSocket` を `ws://host:port` に接続し、接続タイムアウトを `ClientBindOptions.ConnectTimeout` で制御する
  - `ReceiveAsync` ループをワーカー `Task` で回し、テキストフレームのみ購読者へ転送、Binary 等は破棄＋ログ
  - `WebSocketException` / `ClientWebSocketException` を捕捉して `ConnectionStateMachine` に通知（再試行は `ClientSessionManager` の責務）
  - Dispose 後は再利用せず、再接続時は新規インスタンス化する
  - 観測可能な完了条件: 単体プロセスでサーバ接続→テキスト送受信→切断再接続の基本フローが動作する
  - _Requirements: 2.1, 2.4, 2.5_
  - _Boundary: WebSocketClient_

- [ ] 4.5 WebSocketTransportAdapter の統合（server + client ファクトリ）
  - `ITransportAdapter` を実装し、`StartServerAsync` で `WebSocketServer` を起動、`ConnectClientAsync` で `WebSocketClient` を生成して返す
  - `IMessageCodec` を注入し、エンベロープのバイト列をテキストフレームとして送信する（opcode 0x1 固定）
  - `IAsyncDisposable` で全サブリソース（サーバ / クライアント / スレッド / ソケット）の対称解放を行う
  - 同一プロセス内で WebSocket サーバ + クライアントを並行起動し、ラウンドトリップで 1000 件連続送信して欠落無しを確認する PlayMode テストを含める
  - 観測可能な完了条件: 1000 件連続ラウンドトリップテストが欠落無しで緑
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.8_

---

## 5. テスト用ループバックトランスポートと設定・診断レイヤ

- [ ] 5.1 (P) InMemoryLoopbackTransport の実装
  - `ITransportAdapter` を実装し、server/client 方向の `Channel<byte[]>` 2 本でエンベロープ byte 列を直接交換する
  - 実 WebSocket フレーミングを使わず、プロダクション runtime では使用しない（テスト・自己ループ検証専用）
  - `IClientConnection.SendAsync` / `ReceiveAsync` の契約に準拠し、`CoreIpcBus` から透過的に使える
  - 単体テストで、state/event/request-response の自己ループ往復を検証する
  - 観測可能な完了条件: 自己ループ往復テストが全 kind で緑
  - _Requirements: 8.1_
  - _Boundary: InMemoryLoopbackTransport_

- [ ] 5.2 (P) CoreIpcConfigLoader と CoreIpcConfigAsset の実装
  - `CoreIpcConfigAsset` を ScriptableObject として定義し、`CoreIpcOptions` の全フィールドをインスペクタから設定可能にする
  - `CoreIpcConfigLoader.Load()` で 3 階層フォールバック読込を行う：(1) `Resources.Load<CoreIpcConfigAsset>("CoreIpcConfig")` → (2) `StreamingAssets/core-ipc-config.json` → (3) `%AppData%/VTuberSystemBase/core-ipc-config.json`
  - 各層の未指定フィールドは下位層で補完（部分上書き）する
  - 設定ファイル不在時はコード埋め込みの既定値にフォールバックする
  - Editor テストで、3 階層フォールバックと部分上書きの優先順位を検証する
  - 観測可能な完了条件: 5 本程度のフォールバックテストが緑で、`%AppData%` での port 変更が実効する
  - _Requirements: 2.7, 6.1, 7.4_
  - _Boundary: CoreIpcConfigLoader_

- [ ] 5.3 (P) CoreIpcLogger の実装（ログレベルフィルタ）
  - `UnityEngine.Debug.Log` / `LogWarning` / `LogError` のラッパとしてログレベルフィルタを実装する
  - `CoreIpcOptions.LogLevel` を実行時に変更可能とし、`Trace` / `Debug` / `Info` / `Warning` / `Error` の 5 段階をサポートする
  - 接続開始・確立・切断・再接続の構造化ログ形式（`kind` / `topic` / `correlationId` を含む）を提供する
  - メイン出力サーフェスへの描画を行わない（GUI を持たないことで構造的に保証）
  - 単体テストで、ログレベル未満のメッセージが出力されないこと、切替後に反映されることを検証する
  - 観測可能な完了条件: ログレベルフィルタのテストが緑
  - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - _Boundary: CoreIpcLogger_

- [x] 5.4 (P) CoreIpcDiagnostics の実装
  - `IConnectionDiagnostics` を実装し、`CurrentState` / `ReconnectAttemptCount` / `PendingRequestCount` / `StateSlotCount` / `EventQueueCount` / `ConnectedClientCount` のライブプロパティを公開する
  - `ConnectionStateChanged` イベントを `ConnectionStateMachine` の遷移に接続する
  - `TakeSnapshot()` で現時点の全指標をイミュータブル `DiagnosticsSnapshot` として返す
  - 単体テストで、状態変化が診断 API に反映されること、スナップショットが一貫した値を返すことを検証する
  - 観測可能な完了条件: 診断 API のテストが緑で、`ConnectionStateChanged` が遷移ごとに 1 回だけ発火する
  - _Requirements: 5.6, 7.5_
  - _Boundary: CoreIpcDiagnostics_

---

## 6. ライフサイクル管理（PlayerLoop 連携、PlayMode ブリッジ、起動ブートストラップ）

- [ ] 6.1 PlayerLoopInstaller の実装（PreUpdate への対称挿入）
  - `PlayerLoop.GetCurrentPlayerLoop()` を取得し、`PreUpdate` 配下に `IpcDispatchStep` 型の subSystem を追加して `PlayerLoop.SetPlayerLoop(modified)` で反映する
  - `Uninstall()` で挿入したステップを除去し、複数回呼び出しに対する冪等性を保つ
  - 二重挿入検出時は警告ログ出力＋既存置換、`IsInstalled` プロパティで現状を返す
  - PlayMode テストで、PlayMode 開始→停止→再開始を 5 回繰り返してもソケット/スレッド/メモリリークが発生しないことを検証する
  - 観測可能な完了条件: PlayMode 繰返しテストが緑で、`PlayerLoopInstaller.IsInstalled` が対称に遷移する
  - _Requirements: 1.7, 4.6_
  - _Boundary: PlayerLoopInstaller_

- [x] 6.2 IpcDispatchStep と MainThreadDispatchQueue 連動の配線
  - `IpcDispatchStep` を PlayerLoopSystem に挿入可能な `updateDelegate` 付き subSystem として定義する
  - `PreUpdate` フェーズで `MainThreadDispatchQueue.Flush()` を呼び、state coalesce + event FIFO + request/response 解決を 1 フレームで消化する
  - Flush 内例外は各ハンドラ単位で隔離し、`IpcDispatchStep` 自体は例外を投げない
  - 観測可能な完了条件: PlayMode テストで `PublishState` 送信が 1 フレーム以内に購読ハンドラに届き、`PublishEvent` の FIFO 順序が保存される
  - _Requirements: 1.7, 9.1, 9.2_

- [ ] 6.3 CoreIpcRuntime の実装（単一ライフサイクルとサーバ／クライアント同時起動）
  - `ICoreIpcRuntime` を実装し、`InitializeAsync(options)` でサーバとクライアントを同時起動、`PlayerLoopInstaller.Install` で配信ステップを挿入する
  - `State` 遷移を `NotInitialized → Initializing → Running → ShuttingDown → Disposed` で管理し、二重初期化時は `InvalidOperationException`
  - `Dispose` はソケット閉鎖、スレッドキャンセル、キューフラッシュ、`PlayerLoopInstaller.Uninstall` を全て実行し冪等化する
  - `CoreIpcRuntime.Current` を静的 Singleton 入口として公開し、`OverrideForTesting(runtime)` / `ResetForTesting()` でテスト差し替え可能とする
  - PortInUse エラーは例外で throw し、起動元が捕捉して診断ログを出す契約とする（描画には影響させない）
  - 観測可能な完了条件: 単体テストで初期化→Dispose のライフサイクル、二重初期化拒否、Dispose 冪等性、PortInUse の伝搬が緑
  - _Requirements: 4.1, 4.5, 4.7, 4.9, 2.6_
  - _Boundary: CoreIpcRuntime_

- [ ] 6.4 RuntimeBootstrap の実装（RuntimeInitializeOnLoadMethod 経由の自動起動）
  - `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` で `CoreIpcConfigLoader.Load()` → `CoreIpcRuntime.InitializeAsync(options)` を呼ぶ
  - `Application.quitting` を購読し、`CoreIpcRuntime.Current?.Dispose()` を呼んで安全シャットダウンする
  - Unity 仕様により Edit モードでは呼ばれないため、Edit モード非起動（Req 4.8）が構造的に保証される
  - スタンドアロンビルド起動時と Editor PlayMode 開始時で同じ経路を通ることを確認する
  - 観測可能な完了条件: PlayMode テストと standalone ビルドで、シーンロード前に runtime が `Running` 状態に遷移する
  - _Requirements: 4.2, 4.3, 4.5, 4.7, 4.8_

- [ ] 6.5 EditorPlayModeBridge の実装（#if UNITY_EDITOR）
  - `[InitializeOnLoad]` で Editor ドメイン起動時に自身を登録し、`EditorApplication.playModeStateChanged` を購読する
  - `PlayModeStateChange.ExitingPlayMode` 時に `CoreIpcRuntime.Current?.Dispose()` を呼び、`Application.quitting` より早くクリーンアップを完了する
  - `PlayerLoopInstaller.Uninstall()` を確実に呼んでソケット/スレッドリークを防ぐ
  - ドメインリロード跨ぎの状態維持は行わず、再開時は新しいライフサイクルで初期化される
  - PlayMode テストで、PlayMode 開始→停止を 5 回繰り返してもポート `61874` が継続バインド可能であることを検証する
  - 観測可能な完了条件: PlayMode 繰返し後にポート解放状態が維持され、再起動時に再バインドが成功する
  - _Requirements: 4.4, 4.6, 4.9, 5.8_

---

## 7. 結合と PlayMode 手動検証サンプル

- [ ] 7.1 エンドツーエンド配線（Runtime ⇄ Bus ⇄ Transport ⇄ Codec ⇄ DispatchQueue）
  - `CoreIpcRuntime.InitializeAsync` 内で、`WebSocketTransportAdapter`・`SystemTextJsonCodec`・`MainThreadDispatchQueue`・`RequestCorrelationRegistry`・`TopicSubscriptionRegistry`・`ClientSessionManager`・`ConnectionStateMachine`・`CoreIpcDiagnostics` を生成して `CoreIpcBus` に注入する
  - 受信経路（Transport → Codec.Decode → DispatchQueue.Enqueue → Flush → Subscription）を end-to-end で接続する
  - 送信経路（Bus.Publish* → Codec.Encode → Transport.Send → Peer Transport.Receive）を end-to-end で接続する
  - Request/Response 経路（Bus.RequestAsync → CorrelationRegistry → Transport → Peer Transport → Handler → Response → CorrelationRegistry.MatchResponse → TCS 完了）を接続する
  - 観測可能な完了条件: WebSocket トランスポート越しに state/event/request-response がメインスレッド上のハンドラまで届くことが PlayMode テストで確認できる
  - _Requirements: 1.1, 1.3, 1.4, 1.5, 1.7, 2.5, 3.3, 3.4, 9.3_
  - _Depends: 2.5, 3.3, 4.5, 6.3_

- [x] 7.2 最小サンプルシーンと手動検証手順の整備
  - `Packages/com.vtuber-system-base.core-ipc-foundation/Samples~/MinimalLoopback/` に PlayMode で開ける検証シーンを配置する
  - サンプルシーン内で、自己ループ（単一 Unity プロセスでサーバ＋クライアント起動）の state/event/request-response デモを行う MonoBehaviour を含める
  - Unity コンソールに通信ログが出ることを確認する手順を README に記す
  - `%AppData%/VTuberSystemBase/core-ipc-config.json` で port を変更して起動し、変更値でバインドされることの検証手順を含める
  - 観測可能な完了条件: サンプルシーンを PlayMode で開くと、Console にラウンドトリップログが連続出力される
  - _Requirements: 8.3, 8.4, 2.7_
  - _Depends: 7.1_

---

## 8. 検証フェーズ（機能テスト・回帰テスト・ライフサイクルテスト）

- [ ] 8.1 (P) LoopbackRoundTrip / CoalesceSemantics / FifoOrdering / RequestTimeout 統合テスト
  - `InMemoryLoopbackTransport` を用いて state/event/request-response の完全往復を検証する
  - 同一 topic の state 100 件連続送信で、1 フレームあたり最新 1 件のみ購読ハンドラに届くことを検証する
  - event 1000 件連続送信で、受信順が送信順と完全一致することを検証する
  - Request タイムアウトが既定 5 秒と `RequestOptions.Timeout` 上書きの両方で発火することを検証する
  - 観測可能な完了条件: 4 系統の統合テストが PlayMode ランナーで緑
  - _Requirements: 8.1, 8.2, 1.3, 1.4, 1.5, 9.1, 9.2, 9.3_
  - _Boundary: LoopbackRoundTripTests, CoalesceSemanticsTests, FifoOrderingTests, RequestTimeoutTests_
  - _Depends: 7.1_

- [ ] 8.2 (P) ReconnectBackoff / MessageSizeLimit / SchemaEvolution 統合テスト
  - クライアント先行起動→サーバ後発起動シナリオで、バックオフを経て接続成功することを検証する
  - サーバ永久不在で 20 回試行後に `PermanentlyDisconnected` 遷移通知が出ることを検証する
  - 送信側で 1 MB 超過メッセージが `SizeLimitExceeded` で拒否されること、受信側で 1 MB 超過フレームが破棄＋ログ出力されることを検証する
  - 未知フィールド付き JSON を受信しても decode が成功し、既知フィールドが正しく復元されることを検証する
  - 観測可能な完了条件: 4 系統の統合テストが緑
  - _Requirements: 5.2, 5.3, 5.5, 3.9, 3.10, 3.7, 8.2_
  - _Boundary: ReconnectBackoffTests, MessageSizeLimitTests, SchemaEvolutionTests_
  - _Depends: 7.1_

- [ ] 8.3 PlayModeLifecycle 統合テスト
  - PlayMode 開始→停止→再開始を 5 回繰り返し、ソケット/スレッド/メモリリークが発生しないことを検証する
  - 各 PlayMode 区間で `CoreIpcRuntime.State` が `Running` に到達し、停止時に `Disposed` へ遷移することを確認する
  - PlayMode 停止時の切断が再接続を試行せず、通常シャットダウンとして扱われることを検証する（Req 5.8）
  - 繰り返し後にポート `61874` が継続バインド可能であることを検証する
  - Edit モードでは runtime が起動しないことを検証する（`CoreIpcRuntime.Current.State == NotInitialized`）
  - 観測可能な完了条件: 5 回繰り返しテストが緑で、プロセスメモリの継続的増加が観測されない
  - _Requirements: 4.3, 4.4, 4.6, 4.8, 4.9, 5.8_
  - _Depends: 6.5, 7.1_

- [ ] 8.4* パフォーマンス負荷テスト（state 高頻度 coalesce / event 流量 / Request 並行）
  - 1 topic に対し 100 Hz × 10 秒の state 送信で、受信側 Flush あたり高々 1 件・メモリ線形増加なしを確認する
  - 100 Hz × 60 秒で 6000 件の event を送受信し、全件 FIFO で到着することを確認する
  - 100 並行 Request を 5 秒タイムアウトで発行し、全件がタイムアウトまたは正常応答で完了（漏れなし）することを確認する
  - 観測可能な完了条件: 3 系統の性能テストが設計目標値（1 フレーム 16 ms 内配信、メモリ 10 MB 未満）を満たす
  - MVP 優先時の付加的テストカバレッジのため optional としてマーク
  - _Requirements: 9.4, 9.2, 1.5_
  - _Depends: 7.1_
