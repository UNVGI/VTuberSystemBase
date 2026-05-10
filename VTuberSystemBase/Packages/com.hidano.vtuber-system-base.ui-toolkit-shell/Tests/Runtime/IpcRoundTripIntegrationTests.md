# IPC モック注入による送信↔受信 round-trip 結合テスト手順書

本ドキュメントは Task 12.5（Integration: IPC モック注入による送信↔受信 round-trip 結合テスト）の検証手順を記録する。コードは [`IpcRoundTripIntegrationTests.cs`](./IpcRoundTripIntegrationTests.cs)。

---

## 目的（Requirements）

- **Requirement 10.3** — `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8: `InMemoryLoopbackTransport`）を活用して、ダミーコマンドを自プロセス内で送受信し、Command 送信 API と受信購読 API が正しく機能することを検証する手段を備える。
- **Requirement 10.5** — 本シェル単体のテスト実行で、Command 送信 API の呼び分けと受信購読 API の Completion 配信を検証するテストケースを提供する。
- **Requirement 10.6** — IPC クライアント部分について、テスト時に差し替え可能なモック実装（`core-ipc-foundation` の抽象インタフェース `ICoreIpcBus` に対するテストダブル）を受け入れる構造を備える。

## 自己ループ機構の依拠

`core-ipc-foundation` は spec #1 Requirement 8 として `InMemoryLoopbackTransport` を提供する（`InMemoryLoopbackTransportTests.SelfLoop_RoundTripsEventEnvelope` / `SelfLoop_RoundTripsRequestThenResponse` で確認済み）。本テストはトランスポート層を直接利用せず、UI 層の Facade 越しに同等の round-trip 振る舞いを検証するため、`FakeIpcClient.SendInterceptor` を用いて以下の自己ループを構築する:

1. `UiCommandClient.PublishState` / `PublishEvent` → `ICoreIpcBus.PublishState` / `PublishEvent`
2. `FakeIpcClient.SendInterceptor` が outbound 送信を検出し、同 topic / 同 payload で `InjectState` / `InjectEvent` を発火
3. `ICoreIpcBus.SubscribeState` / `SubscribeEvent` 経由で `UiSubscriptionClient` がメッセージを受け取り
4. `MessageEnvelope<TPayload>` を組み立てて UI 側 callback に配信

トランスポートに到達しない構成のため、本シェルが `core-ipc-foundation` の具体実装 asmdef に依存しないという構造制約（Requirement 5.10, 1.5）と整合する。

## 検証スコープ

| ケース | 観測点 |
| --- | --- |
| `PublishState_RoundTripsThroughSubscriptionFacade` | `PublishState` した payload が `Subscribe(MessageKind.State)` の callback に envelope 付きで届く |
| `PublishEvent_RoundTripsToEventSubscribersOnly` | `PublishEvent` は Event 購読者にのみ届き、State 購読には漏れない |
| `PublishState_MultiplePublishes_AreAllDeliveredInOrder` | 連続 5 件の Publish が同順で届く |
| `PublishState_FanOutToMultipleSubscribers` | 同一 topic を購読する複数 callback すべてに届く |
| `PublishState_DifferentTopics_DoNotCrosstalk` | 異なる topic 同士でメッセージが混線しない |
| `DisposedSubscription_StopsReceivingRoundTrippedMessages` | 購読 token Dispose 後は callback が起動しない |
| `RoundTrip_EmitsSendAndReceiveLogsInIpcCategory` | `SendStarted` / `SendResult` / `Received` ログが `LogCategory.Ipc` で記録される（Req 11.4, 11.5） |
| `DisconnectedBus_PublishStateShortCircuits_NoRoundTrip` | 切断中は `UiCommandClient` が `NotConnected` を返却し、自己ループは起動しない（Req 9.4） |
| `RequestAsync_RoundTripsThroughRegisteredHandler` | `RegisterRequestHandler` 経由で Response が返り、`RequestAsync` が `Success` で完了する |

## 実行コマンド

EditMode テストランナーで本クラス単体を実行する場合（バッチモード）:

```
"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" \
  -batchmode -nographics \
  -projectPath D:/Personal/Repositries/VTuberSystemBase \
  -runTests -testPlatform EditMode \
  -assemblyNames UiToolkitShell.Tests \
  -testFilter "VTuberSystemBase.UiToolkitShell.Tests.Runtime.IpcRoundTripIntegrationTests" \
  -testResults <results.xml> -logFile <log>
```

`UiToolkitShell.Tests` アセンブリ全体で実行する場合は `-testFilter` を省略する。

## 観測可能な完了状態

- `IpcRoundTripIntegrationTests` の全テストが緑になる。
- ダミーコマンドの送受信が UI 単体で完結する（`core-ipc-foundation` の具体実装に依存しない）。
- Wave 2 完了判定が後続 spec（#4〜#6）に依存しないことを保証する（Req 10.1）。
