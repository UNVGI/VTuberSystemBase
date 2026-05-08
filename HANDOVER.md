# セッション引き継ぎノート

## ◯ 今回やったこと

- 別リポジトリから `com.hidano.vtuber-system-base.core-ipc-foundation` を参照したときの一連のコンパイルエラーを修正
- `CS0012`（`JavaScriptEncoder` の参照不足）→ asmdef を auto-reference 依存から明示参照に切り替え
- `CS0433`（`IAsyncDisposable` / `IAsyncEnumerable<T>` の二重定義）→ `Microsoft.Bcl.AsyncInterfaces.dll` をポリフィルとして除外
- `CS0234` / `CS0246`（`OutputRendererShell.Runtime` で `System.Text.Json` が見えない）→ 直接利用 asmdef にも明示参照を追加
- 全パッケージ横断で「直接 `System.Text.Json` 型を触る asmdef」を棚卸しし、`UiToolkitShell` 系・`CoreIpc.Editor` / Samples は対応不要と確認

## ◯ 決定事項

- Plugin DLL は **明示参照（`overrideReferences: true` + `precompiledReferences` に列挙）で固定**する。auto-reference には頼らない。
- 同梱する DLL は 4 つのまま (`System.Text.Json.dll` / `System.Text.Encodings.Web.dll` / `System.Runtime.CompilerServices.Unsafe.dll` / `Microsoft.Bcl.AsyncInterfaces.dll`)。`Microsoft.Bcl.AsyncInterfaces.dll` だけは meta を `isExplicitlyReferenced: 1` にして、誰も明示参照しない。ファイルは `System.Text.Json.dll` の strong-name 参照のため残す。
- 新しい asmdef を作るときの判定: コード中で `JsonElement` / `JsonSerializer` / `JsonDocument` 等を**直接**使うなら `precompiledReferences` に追加。`MessageEnvelope` 経由で間接的に持つだけなら不要。
- Unity 6 (.NET Standard 2.1) では `Microsoft.Bcl.AsyncInterfaces.dll` を **絶対に明示参照しない**（必ず `netstandard.dll` と衝突する）。

## ◯ 捨てた選択肢と理由

- **`Microsoft.Bcl.AsyncInterfaces.dll` をパッケージから削除する案** → `System.Text.Json.dll` が strong-name でこのアセンブリを参照しているため、削除すると runtime で `FileNotFoundException` の可能性。型解決は `netstandard.dll` 側に行くがアセンブリのロード自体は要求されるので「ファイルは残す・参照しない」に着地。
- **参照側プロジェクトに DLL を別途配置してもらう案** → ユーザーが明確に拒否。同梱で完結させる方針で確定。
- **すべての asmdef に予防的に `System.Text.Json` 参照を入れる案** → C# コンパイラは直接使わない型は要求しないので過剰。判定基準だけ決めて必要な箇所のみ。

## ◯ ハマりどころ

- 「同梱されているのにエラー」の症状は `isExplicitlyReferenced: 0`（auto-reference）に頼っているのが根本原因。参照側の他パッケージとの DLL 衝突や API Compatibility Level の差で簡単に壊れる。
- `precompiledReferences` に `Microsoft.Bcl.AsyncInterfaces.dll` を入れた瞬間、`netstandard.dll` の同型と衝突して `CS0433` 多発。**Tests asmdef に元から書かれていた**ので、そこから連鎖エラーが起きた。
- 1 ヶ所直すと別 asmdef のエラーが顕在化する“もぐら叩き”状態になりやすい。最終的に全パッケージ横断で直接利用箇所を grep で洗い出して終結。

## ◯ 学び

- Unity の `precompiledReferences` 暗黙挙動は、参照側プロジェクトの構成次第で簡単に壊れる。**配布パッケージは必ず明示参照に倒す**のが鉄則。
- `Microsoft.Bcl.AsyncInterfaces` は .NET Standard 2.0 用ポリフィル。Unity 6 (.NET Standard 2.1) では BCL に同型が存在するので、**ファイルは置くが参照はしない**運用にする。
- 「直接利用 (`using System.Text.Json`)」と「間接利用（`MessageEnvelope` を変数で受けるだけ）」では参照要件が違う。コンパイラは触らない型を要求しないので、後者は明示不要。
- 新規 `.meta` の GUID を作るときはランダムな 32 桁 hex を都度生成する（連続パターンやローテーション系列は禁止 — グローバル `CLAUDE.md` の追加ルール参照）。

## ◯ 次にやること

### P1（即やる）

- 参照側リポジトリで Library フォルダを削除して Unity を再起動し、3 種のエラー (`CS0012` / `CS0433` / `CS0234`+`CS0246`) が消えることを確認

### P2（中優先）

- 今回の変更を git にコミット（`package.json` のバージョンバンプを 0.1.0 → 0.1.1 で検討）

### P3（低優先）

- `core-ipc-foundation` の README / docs に「Plugin DLL の同梱方針」「新規 asmdef を増やすときの判定ルール」を追記

## ◯ 関連ファイル

修正した asmdef:
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Abstractions/VTuberSystemBase.CoreIpc.Abstractions.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Core/VTuberSystemBase.CoreIpc.Core.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Tests/Editor/VTuberSystemBase.CoreIpc.Tests.Editor.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Tests/Runtime/VTuberSystemBase.CoreIpc.Tests.Runtime.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.output-renderer-shell/Runtime/VTuberSystemBase.OutputRendererShell.Runtime.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.output-renderer-shell/Tests/PlayMode/VTuberSystemBase.OutputRendererShell.PlayModeTests.asmdef`
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.output-renderer-shell/Tests/EditMode/VTuberSystemBase.OutputRendererShell.EditModeTests.asmdef`

修正した meta:
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Plugins/SystemTextJson/Microsoft.Bcl.AsyncInterfaces.dll.meta`

参照だけしたファイル:
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Core/Codec/SystemTextJsonCodec.cs:146`（`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`）
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Abstractions/ITransportAdapter.cs:9,20,26`（`IAsyncDisposable` / `IAsyncEnumerable<T>`）
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.output-renderer-shell/Runtime/Dispatch/OutputCommandDispatcher.cs`（`JsonElement` / `JsonSerializerOptions`）
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/Runtime/Commands/MessageEnvelope.cs`（独自定義の `MessageEnvelope<TPayload>`、System.Text.Json 非依存）
