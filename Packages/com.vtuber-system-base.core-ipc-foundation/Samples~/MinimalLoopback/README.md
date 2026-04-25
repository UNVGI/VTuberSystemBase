# Minimal Loopback サンプル

`com.vtuber-system-base.core-ipc-foundation` の自己ループ検証用サンプルです。
単一の Unity プロセス内で WebSocket サーバとクライアントが同居して起動する CoreIpc
ランタイムを通じて、State / Event / Request-Response の往復が成立することを Unity
コンソール上で確認できます。

## サンプルに含まれるもの

- `MinimalLoopback.unity` — PlayMode で開く検証用シーン。
- `Scripts/MinimalLoopbackDemo.cs` — `CoreIpcRuntime.Current` 経由で `Bus` を取得し、
  購読登録 → 周期的な Publish / Request 発行 → 受信ログ出力を行う `MonoBehaviour`。
- `Scripts/VTuberSystemBase.CoreIpc.Samples.MinimalLoopback.asmdef` — サンプル専用 asmdef。
  Core/Abstractions のみを参照する。

## 手順

1. Unity Package Manager で
   `com.vtuber-system-base.core-ipc-foundation` を選択し、
   *Samples* タブから **Minimal Loopback** をインポートする。
   インポート先は `Assets/Samples/VTuberSystemBase Core IPC Foundation/<version>/Minimal Loopback/`。
2. `MinimalLoopback.unity` を開く。
3. PlayMode を開始する。
4. Unity コンソールに次のようなログが連続出力されることを確認する：
   - `[CoreIpc.RuntimeBootstrap] CoreIpcRuntime initialization completed.`
   - `[MinimalLoopback] subscriptions wired (state=demo/state, event=demo/event, request=demo/echo); endpoint=ws://127.0.0.1:61874`
   - `[MinimalLoopback][state:demo/state] received Counter=N Message='state-N'`
   - `[MinimalLoopback][event:demo/event] received Counter=N Message='event-N'`
   - `[MinimalLoopback][request:demo/echo] response Counter=N Message='echo:request-N'`
5. PlayMode を停止する。`EditorPlayModeBridge` がランタイムを Dispose し、ポートが解放されることを確認する。

## ポート上書き検証手順（Req 2.7）

設定ファイルによる上書きが効くことを以下の手順で検証できる。

1. `%AppData%\VTuberSystemBase\core-ipc-config.json` を作成する
   （Windows での実体は `C:\Users\<user>\AppData\Roaming\VTuberSystemBase\core-ipc-config.json`）。
2. 内容を次のようにする（ポートを既定値 `61874` から `62000` へ変更する例）：
   ```json
   {
     "port": 62000
   }
   ```
3. Unity Editor を再起動するか、PlayMode を一旦停止してから再開する。
4. Unity コンソールのログ
   `[MinimalLoopback] subscriptions wired (...); endpoint=ws://127.0.0.1:62000`
   から、`CoreIpcConfigLoader` が `%AppData%` の値で上書きしたポートでバインドされたことを確認する。
5. 検証後はファイルを削除するか `port` を既定値に戻し、他環境への意図しない設定持ち込みを防ぐ。

## 想定するログ系統

- `Debug.Log` — 配信成功時の State / Event / Response の本体。
- `Debug.LogWarning` — `Publish*` が `NotConnected` 等で失敗したときの理由。
- `Debug.LogError` — `runtimeWaitTimeoutSeconds` を超えてもランタイムが `Running` に到達しなかった場合や、
  Request 発行で例外が発生した場合の診断情報。

## 注意点

- 本サンプルは `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 経由で
  `RuntimeBootstrap` が起動する CoreIpc ランタイムに依存する。Edit モードでは
  ランタイムが起動しないため、必ず PlayMode で確認すること。
- ポート占有時は `[CoreIpc.RuntimeBootstrap] CoreIpcRuntime initialization failed: ...`
  というエラーがコンソールに出る。設定ファイルで `port` を変更するか、占有プロセスを停止して再試行する。
