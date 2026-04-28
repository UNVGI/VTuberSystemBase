#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;

namespace VTuberSystemBase.UiToolkitShell.CommonUi
{
    /// <summary>
    /// 4 共通コントロール（<see cref="VsbSlider"/>, <see cref="VsbColorPicker"/>,
    /// <see cref="VsbNumberedList"/>, <see cref="VsbToggleGroup"/>）の
    /// <c>UxmlFactory</c> 登録と既定 USS 一覧の単一参照点 (Requirement 7.2, 7.5;
    /// design.md §CommonUi §CommonUiRegistration)。<see cref="RegisterAll"/> は
    /// <c>UiShellBootstrapper</c> の初期化時に 1 度だけ呼び出され（task 10.1）、
    /// 以降は冪等に動作する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UxmlFactory 登録経路</b>: 各コントロールは入れ子クラスとして
    /// <c>public new class UxmlFactory : UxmlFactory&lt;TControl, UxmlTraits&gt;</c>
    /// を公開し、UI Toolkit のレガシー <see cref="UnityEngine.UIElements.UxmlFactory{TCreatedType, TTraits}"/>
    /// 系がアセンブリ走査でこれを自動登録する。<see cref="RegisterAll"/> は
    /// 4 コントロールの型を明示的に touch して <c>RuntimeTypeHandle</c> 解決を
    /// 強制し、ランタイムでアセンブリの型ロードが行われる経路を 1 箇所に固定する
    /// （ブートストラップ時点で `&lt;vsb:VsbSlider /&gt;` 等の参照が必ず解決される
    /// ことを観測可能にする）。
    /// </para>
    /// <para>
    /// <b>既定 USS</b>: <see cref="DefaultStyleSheetAssetPaths"/> は
    /// パッケージ内の 4 個の <c>.uss</c> ファイルを <c>Packages/...</c>
    /// 形式の AssetDatabase パスで列挙する。<c>UiShellBootstrapper</c> はこの
    /// パス列を <see cref="UnityEngine.UIElements.StyleSheet"/> としてロードし、
    /// ルート <c>VisualElement.styleSheets</c> に積むことで利用者プロジェクトの
    /// スキン USS が無い場合でも 4 コントロールが視認可能な既定スタイルで描画される
    /// （design.md §Implementation Notes / §Skin USS セレクタ命名規約）。
    /// </para>
    /// <para>
    /// 本クラスは静的状態のみを持ち、Unity のドメインリロードで
    /// <see cref="IsRegistered"/> はリセットされる。複数回呼び出しても副作用は
    /// 発生しない。
    /// </para>
    /// </remarks>
    public static class CommonUiRegistration
    {
        private static readonly Type[] _controlTypes =
        {
            typeof(VsbSlider),
            typeof(VsbColorPicker),
            typeof(VsbNumberedList),
            typeof(VsbToggleGroup),
        };

        private const string PackageRoot =
            "Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell";

        private static readonly string[] _defaultStyleSheetAssetPaths =
        {
            PackageRoot + "/Runtime.CommonUi/Controls/VsbSlider.uss",
            PackageRoot + "/Runtime.CommonUi/Controls/VsbColorPicker.uss",
            PackageRoot + "/Runtime.CommonUi/Controls/VsbNumberedList.uss",
            PackageRoot + "/Runtime.CommonUi/Controls/VsbToggleGroup.uss",
        };

        /// <summary>
        /// <see cref="RegisterAll"/> が成功完了済みなら <c>true</c>。
        /// <c>UiShellBootstrapper</c> 起動時 1 回呼出契約の遵守を確認するための
        /// 観測ポイント。ドメインリロードでリセットされる。
        /// </summary>
        public static bool IsRegistered { get; private set; }

        /// <summary>
        /// UxmlFactory 登録対象の 4 コントロール型を読み取り専用で公開する。
        /// 順序は task 7.1〜7.4 の宣言順（Slider → ColorPicker → NumberedList →
        /// ToggleGroup）に固定し、ログ・診断スナップショットでの並びを安定化する。
        /// </summary>
        public static IReadOnlyList<Type> RegisteredControlTypes => _controlTypes;

        /// <summary>
        /// 既定 USS の AssetDatabase パス（<c>Packages/...</c> 形式）。
        /// <c>UiShellBootstrapper</c> はこのパス列を StyleSheet としてロードして
        /// ルートに積み、利用者スキンが上書きするまでの既定見た目を提供する。
        /// </summary>
        public static IReadOnlyList<string> DefaultStyleSheetAssetPaths => _defaultStyleSheetAssetPaths;

        /// <summary>
        /// 4 コントロールの UxmlFactory と既定 USS パスを一括登録する。
        /// <c>UiShellBootstrapper</c> 初期化時に 1 度だけ呼ぶ契約だが、
        /// 冪等に実装してあるため何度呼んでも副作用はない（防御的安全網）。
        /// </summary>
        public static void RegisterAll()
        {
            if (IsRegistered)
            {
                return;
            }

            // RuntimeTypeHandle を解決して各 UxmlFactory<T,U> の入れ子型を含む
            // 親型の型ロードを強制する。レガシー UxmlFactory はアセンブリ走査で
            // 自動登録されるが、ブートストラップ時点で `<vsb:VsbSlider />` 等が
            // 確実に解決される経路をここで固定する。
            for (var i = 0; i < _controlTypes.Length; i++)
            {
                _ = _controlTypes[i].TypeHandle;
            }

            IsRegistered = true;
        }
    }
}
