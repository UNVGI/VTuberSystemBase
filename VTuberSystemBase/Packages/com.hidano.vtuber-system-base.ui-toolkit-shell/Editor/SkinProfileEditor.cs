#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Editor
{
    /// <summary>
    /// Inspector custom for <see cref="UiToolkitShellSkinProfile"/> (task 11.2;
    /// design.md §Skin §UiToolkitShellSkinProfile "Inspector 表示は <c>SkinProfileEditor</c>
    /// でガイド付き UX を提供"; Requirement 6.7 "スキン差し替え拡張点を本パッケージを
    /// フォークせずに利用可能な形で提供する", 6.4 "既定の VisualTreeAsset 参照を
    /// 置き換え可能な拡張点").
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inspector adds three pieces of guided UX on top of the default Unity
    /// inspector:
    /// </para>
    /// <list type="number">
    /// <item><description><b>Section headings</b> — Root / Character / Stage Lighting
    /// / Camera Switcher / Common UI を太字ラベルで区切り、利用者が <c>RootStyleSheets</c>
    /// と <c>CharacterTabStyleSheets</c> 等の用途を取り違えないようにする。</description></item>
    /// <item><description><b>Required-field warning banner</b> — <see cref="UiToolkitShellSkinProfile.Validate"/>
    /// が <see cref="Bootstrap.BootstrapErrorCode.SkinProfileMissing"/> を返す状態
    /// （<c>RootVisualTreeAsset</c> が空）を <c>HelpBox</c> で警告し、起動時に
    /// シェルが起動失敗する前に Editor 上で気付ける。</description></item>
    /// <item><description><b>Copy package defaults button</b> — パッケージ同梱の
    /// 既定 UXML/USS（<c>TabBar.uxml</c> / <c>TabBar.uss</c> / <c>EmptyTabShell.uxml</c>）
    /// を空きフィールドへ流し込む。既存値は上書きしない（利用者の作業を保護する）。</description></item>
    /// </list>
    /// <para>
    /// Inspector の描画ロジックと「既定値コピー」ロジックは <see cref="CopyPackageDefaults"/>
    /// と <see cref="HasMissingRequiredField"/> として静的に切り出し、Editor の
    /// IMGUI を介さずにユニットテストから検証可能にしている（Tests/Runtime/SkinProfileEditorTests）。
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(UiToolkitShellSkinProfile))]
    public sealed class SkinProfileEditor : UnityEditor.Editor
    {
        public const string SectionRoot = "Root (タブバー + 通知バー)";
        public const string SectionCharacter = "タブ: Character Selection";
        public const string SectionStageLighting = "タブ: Stage Lighting Volume";
        public const string SectionCameraSwitcher = "タブ: Camera Switcher";
        public const string SectionCommonUi = "共通 UI ライブラリ (USS オーバーライド)";

        public const string MissingRootWarning =
            "RootVisualTreeAsset が未設定です。" +
            "シェル起動時に BootstrapErrorCode.SkinProfileMissing が返り、" +
            "ルート UIDocument が構築されません。" +
            "「既定値をコピー」で同梱の TabBar.uxml を割り当てるか、" +
            "独自の UXML を設定してください。";

        public const string IntroHelp =
            "ui-toolkit-shell のスキン差し替え拡張点 (Requirement 6.3, 6.4, 6.7, 6.8)。\n" +
            "ルート UIDocument と 3 タブ UIDocument の VisualTreeAsset / StyleSheet を" +
            "差し替えます。タブ UXML が null の場合はシェルが空枠 (EmptyTabShell.uxml) に" +
            "フォールバックします (Requirement 10.2)。";

        public const string CopyDefaultsButtonLabel = "既定値をコピー (Copy Package Defaults)";

        private const string PackageRoot =
            "Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/Runtime.UxmlUss";

        public const string DefaultRootUxmlPath = PackageRoot + "/TabBar.uxml";
        public const string DefaultRootUssPath = PackageRoot + "/TabBar.uss";
        public const string DefaultEmptyTabUxmlPath = PackageRoot + "/EmptyTabShell.uxml";

        public override void OnInspectorGUI()
        {
            var profile = (UiToolkitShellSkinProfile)target;
            serializedObject.Update();

            EditorGUILayout.HelpBox(IntroHelp, MessageType.Info);

            if (HasMissingRequiredField(profile))
            {
                EditorGUILayout.HelpBox(MissingRootWarning, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(CopyDefaultsButtonLabel))
                {
                    Undo.RecordObject(profile, "Copy UI Toolkit Shell skin defaults");
                    CopyPackageDefaults(profile);
                    EditorUtility.SetDirty(profile);
                    serializedObject.Update();
                }
            }

            EditorGUILayout.Space();
            DrawSection(SectionRoot, "RootVisualTreeAsset", "RootStyleSheets");
            DrawSection(SectionCharacter, "CharacterTabVisualTreeAsset", "CharacterTabStyleSheets");
            DrawSection(SectionStageLighting, "StageLightingTabVisualTreeAsset", "StageLightingTabStyleSheets");
            DrawSection(SectionCameraSwitcher, "CameraSwitcherTabVisualTreeAsset", "CameraSwitcherTabStyleSheets");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(SectionCommonUi, EditorStyles.boldLabel);
            var commonUiProp = serializedObject.FindProperty("CommonUiStyleSheets");
            if (commonUiProp != null)
            {
                EditorGUILayout.PropertyField(commonUiProp, includeChildren: true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSection(string title, string vtaPropertyName, string ussListPropertyName)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            var vtaProp = serializedObject.FindProperty(vtaPropertyName);
            if (vtaProp != null)
            {
                EditorGUILayout.PropertyField(vtaProp);
            }

            var ussProp = serializedObject.FindProperty(ussListPropertyName);
            if (ussProp != null)
            {
                EditorGUILayout.PropertyField(ussProp, includeChildren: true);
            }
        }

        /// <summary>
        /// Returns the section heading shown in the Inspector for the given tab.
        /// Centralised so tests can pin the per-tab labelling without digging into
        /// IMGUI calls (task 11.2 観測可能な完了状態: 3 タブセクションごとの見出し).
        /// </summary>
        public static string GetTabSectionHeading(Panels.TabId tabId)
        {
            switch (tabId)
            {
                case Panels.TabId.Character:
                    return SectionCharacter;
                case Panels.TabId.StageLighting:
                    return SectionStageLighting;
                case Panels.TabId.CameraSwitcher:
                    return SectionCameraSwitcher;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Mirrors <see cref="UiToolkitShellSkinProfile.Validate"/> as a boolean used by
        /// the warning banner. Surfacing it as a public static keeps the warning
        /// trigger condition trivially testable without spinning up IMGUI.
        /// </summary>
        public static bool HasMissingRequiredField(UiToolkitShellSkinProfile? profile)
        {
            return UiToolkitShellSkinProfile.Validate(profile).HasValue;
        }

        /// <summary>
        /// Fills any empty fields on <paramref name="profile"/> with the package-shipped
        /// defaults. Pre-populated fields are preserved so users do not lose the assets
        /// they already wired up. Returns the count of fields actually written so tests
        /// can assert idempotency.
        /// </summary>
        public static int CopyPackageDefaults(UiToolkitShellSkinProfile profile)
        {
            if (profile == null) return 0;

            var written = 0;

            if (profile.RootVisualTreeAsset == null)
            {
                var defaultRoot = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultRootUxmlPath);
                if (defaultRoot != null)
                {
                    profile.RootVisualTreeAsset = defaultRoot;
                    written++;
                }
            }

            if (profile.RootStyleSheets == null)
            {
                profile.RootStyleSheets = new List<StyleSheet>();
            }

            if (profile.RootStyleSheets.Count == 0)
            {
                var defaultRootUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(DefaultRootUssPath);
                if (defaultRootUss != null)
                {
                    profile.RootStyleSheets.Add(defaultRootUss);
                    written++;
                }
            }

            var emptyTab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultEmptyTabUxmlPath);
            if (emptyTab != null)
            {
                if (profile.CharacterTabVisualTreeAsset == null)
                {
                    profile.CharacterTabVisualTreeAsset = emptyTab;
                    written++;
                }
                if (profile.StageLightingTabVisualTreeAsset == null)
                {
                    profile.StageLightingTabVisualTreeAsset = emptyTab;
                    written++;
                }
                if (profile.CameraSwitcherTabVisualTreeAsset == null)
                {
                    profile.CameraSwitcherTabVisualTreeAsset = emptyTab;
                    written++;
                }
            }

            return written;
        }
    }
}
