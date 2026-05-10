# Stage Addressables Group の構成例

`StageCatalogBuilder` が走査する `stage` ラベルの組み方:

1. `Window > Asset Management > Addressables > Groups` で新規 Group を作成（推奨名: `Stages`）。
2. Stage Prefab（任意の GameObject ヒエラルキー: 床 / 壁 / 装飾 / バックライト等）をドラッグして登録。
3. 各 Prefab エントリのラベルに `stage` を付与する（複数ラベル可）。
4. 必要に応じて `<primaryKey>.thumbnail` という命名で 256x256 程度の Sprite/Texture をサムネイルとして同 Group に登録すると、UI 側がカタログサムネイルとして拾う。

PlayMode 起動後、`StageCatalogBuilder.BuildAsync` が `stage` ラベルで `LoadResourceLocationsAsync` を呼び、登録された全 PrimaryKey を `StageCatalogDto.Items` として publish する。
