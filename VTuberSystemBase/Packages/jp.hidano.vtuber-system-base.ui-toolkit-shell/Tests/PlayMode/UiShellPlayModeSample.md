# UiShellPlayModeSample 手動検証手順 (task 12.7 / Requirement 10.4)

`UiShellPlayModeSample.unity` は `ui-toolkit-shell` 単独 PlayMode 起動を目視で確認するための最小シーンである。後続 spec (#4〜#6) のタブ実装が無くても、以下の挙動が観察できる。

- ルート UIDocument が Display 1 に出現する (Requirement 1.1, 1.2)
- 3 タブ分のタブバーボタンがプリロード完了後に活性化する (Requirement 3.3)
- ボタンクリックでアクティブタブが切り替わる (Requirement 2.3, 2.9)
- 通知バー領域がレンダリングされ、フェイルセーフ用の通知行を受ける場所がある (Requirement 9.5, 9.6)

シーンは以下の構成で起動する:

| 配線対象 | 値 | 用途 |
| --- | --- | --- |
| `UiShellPlayModeSampleRoot` GameObject | `UiShellPlayModeSampleHost` MonoBehaviour 1 件 | `UiShellLifecycleDriver.Configure(...) → StartShell()` を Awake で実行 |
| `UiShellPlayModeSampleHost.skinProfile` | `Runtime.UxmlUss/DefaultSkinProfile.asset` | Root = `TabBar.uxml`, Root USS = `TabBar.uss`, タブ UXML = `EmptyTabShell.uxml` |
| IPC バス | `FakeIpcClient` (Disconnected 固定) | Requirement 9.1 のフェイルセーフを引き出す |
| Tab Mount Strategy | `FakeTabMountStrategy` | 3 タブ分の `VisualElement` を即座に `NotifyTabMounted` |
| Addressables Initializer | `FakeAddressablesInitializer.Immediate / Ok` | Bootstrap が `BootstrapStep.AddressablesInitialized` を通過する |

## 前提

- Unity 6.3 URP プロジェクトを開いていること
- `com.unity.test-framework` が有効化され `UNITY_INCLUDE_TESTS` define が立っていること
  - 通常は Test Framework パッケージを Package Manager で導入していれば自動で立つ
- `Addressables` パッケージ (`com.unity.addressables` 2.x) が解決済みであること

## 手順

1. **シーンを開く**
   - Unity Editor で `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Tests/PlayMode/UiShellPlayModeSample.unity` を開く
   - ヒエラルキに `UiShellPlayModeSampleRoot` GameObject が 1 件のみ存在することを確認する
   - Inspector 上で `UiShellPlayModeSampleHost` の `Skin Profile` フィールドに `DefaultSkinProfile (UiToolkitShellSkinProfile)` がアサインされていることを確認する

2. **PlayMode に入る**
   - エディタの Play ボタンを押す
   - Console に以下のログが出力されることを確認する (LogLevel.Info で確認可能):
     - `UiShellBootstrapper: shell running.` (LogCategory.Lifecycle)
     - 例外スタックトレースが出ていないこと

3. **タブバーの表示**
   - Game ビュー (Display 1) の上部にダーク基調のタブバーが表示されている
   - 3 つのボタン `Character` / `Stage` / `Camera` が横並びに見える
   - 起動直後はプリロード完了に伴いボタンが活性化している (Requirement 3.3)
   - 初期アクティブタブが `Character` で、`vsb-tab-bar__button--active` クラス相当のハイライトがかかっている

4. **タブ切替の確認**
   - `Stage` ボタンをクリック → アクティブハイライトが `Stage` に移ること
   - `Camera` ボタンをクリック → 同様に `Camera` に移ること
   - `Character` に戻して同様に確認
   - 切替時にゲームビューや Console 上で 1 フレームでもフリーズ/例外が観測されないこと (Requirement 2.9)
   - Console に `LogCategory.TabSwitch` のログ (切替元 / 切替先 / Duration) が出ていること (Requirement 11.2)

5. **通知バー領域の表示**
   - タブコンテンツ領域の下に幅広の通知バー領域 (`vsb-notification-bar`) が描画されている
   - 起動直後は通知行は空でよい (`FakeIpcClient` は Disconnected で固定だが状態遷移を起こしていないため、connection 警告は積まれていない)
   - Inspector → Window → Analysis → UI Toolkit Debugger で `vsb-notification-bar` 要素が存在することを確認できる

6. **PlayMode を停止する**
   - エディタの Stop ボタンを押す
   - Console に `UiShellBootstrapper: shell stopped.` が出力される
   - ヒエラルキに `VsbUiToolkitShellRoot` GameObject が残らない (Requirement 8.3 / 8.4)

## チェックリスト

| # | 観点 | 期待結果 | 結果 |
| --- | --- | --- | --- |
| 1 | 起動 | Play 後 Console に `shell running.` ログが出る | [ ] |
| 2 | タブバー表示 | Display 1 にタブバー (3 ボタン) が見える | [ ] |
| 3 | クリック切替 | `Character → Stage → Camera → Character` で active クラスが追従する | [ ] |
| 4 | 通知バー表示 | `vsb-notification-bar` 領域が UI Toolkit Debugger / 画面下に存在する | [ ] |
| 5 | 例外なし | Play 中・Stop 中ともに NullReferenceException 等が出ない | [ ] |
| 6 | クリーンアップ | Stop 後 `VsbUiToolkitShellRoot` GameObject が残らない | [ ] |

全項目に [x] が入った時点で task 12.7 の手動検証完了とみなす (Requirement 10.4)。

## トラブルシュート

- **タブバーが見えない**: `DefaultSkinProfile.asset` の `RootVisualTreeAsset` が `TabBar.uxml` を指しているか Inspector で確認する。Validate が `BootstrapErrorCode.SkinProfileMissing` を返している場合 Console にエラーが出る。
- **ボタンが押せない**: プリロード完了前は `vsb-tab-bar__button--disabled` で非活性 (Requirement 3.2)。`FakeTabMountStrategy` は同期で 3 タブを Mount するため通常はすぐ活性化する。Console の `LogCategory.Preload` ログで `LoadedCount == 3` を確認する。
- **`Missing (Mono Script)` が表示される**: `UNITY_INCLUDE_TESTS` define が立っていない。Test Framework パッケージを Package Manager で導入し直す。
