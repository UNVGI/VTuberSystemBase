#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Editor;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 11.2: <see cref="SkinProfileEditor"/> (Inspector custom UX) tests.
    /// Pin the CustomEditor binding, the per-tab section heading source of truth,
    /// the warning-banner trigger condition, and the package-defaults copy button
    /// (design.md §Skin §UiToolkitShellSkinProfile; Requirement 6.4, 6.7).
    /// </summary>
    [TestFixture]
    public sealed class SkinProfileEditorTests
    {
        [Test]
        [Description("CustomEditor 属性が宣言されており、Unity が SkinProfile の Inspector として認識する")]
        public void Editor_DeclaresCustomEditorAttribute_AndIsBoundBySerializedObject()
        {
            var attr = typeof(SkinProfileEditor)
                .GetCustomAttributes(typeof(CustomEditor), inherit: false)
                .OfType<CustomEditor>()
                .FirstOrDefault();
            Assert.That(attr, Is.Not.Null,
                "SkinProfileEditor must declare [CustomEditor]");

            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            UnityEditor.Editor? editor = null;
            try
            {
                editor = UnityEditor.Editor.CreateEditor(profile);
                Assert.That(editor, Is.Not.Null,
                    "Unity must be able to construct an editor for the profile");
                Assert.That(editor, Is.InstanceOf<SkinProfileEditor>(),
                    "Unity must dispatch to SkinProfileEditor for UiToolkitShellSkinProfile");
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("Editor 型は UnityEditor.Editor 派生である")]
        public void Editor_DerivesFromUnityEditor_Editor()
        {
            Assert.That(typeof(UnityEditor.Editor).IsAssignableFrom(typeof(SkinProfileEditor)),
                Is.True);
        }

        [Test]
        [Description("3 タブそれぞれにユニークな見出しが定義されている (task 11.2 観測可能な完了状態)")]
        public void GetTabSectionHeading_ReturnsUniqueLabel_ForEachTab()
        {
            var character = SkinProfileEditor.GetTabSectionHeading(TabId.Character);
            var stage = SkinProfileEditor.GetTabSectionHeading(TabId.StageLighting);
            var camera = SkinProfileEditor.GetTabSectionHeading(TabId.CameraSwitcher);

            Assert.That(character, Is.Not.Null.And.Not.Empty);
            Assert.That(stage, Is.Not.Null.And.Not.Empty);
            Assert.That(camera, Is.Not.Null.And.Not.Empty);

            Assert.That(character, Is.EqualTo(SkinProfileEditor.SectionCharacter));
            Assert.That(stage, Is.EqualTo(SkinProfileEditor.SectionStageLighting));
            Assert.That(camera, Is.EqualTo(SkinProfileEditor.SectionCameraSwitcher));

            Assert.That(new[] { character, stage, camera }.Distinct().Count(),
                Is.EqualTo(3),
                "All three tab section headings must be distinct so the user can tell them apart");
        }

        [Test]
        [Description("Root / Common UI セクションラベルも非空（タブ以外の見出しもガイド対象）")]
        public void RootAndCommonUiSectionLabels_AreNonEmpty()
        {
            Assert.That(SkinProfileEditor.SectionRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(SkinProfileEditor.SectionCommonUi, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Description("HasMissingRequiredField は null プロファイルを true（警告対象）と判定する")]
        public void HasMissingRequiredField_NullProfile_IsTrue()
        {
            Assert.That(SkinProfileEditor.HasMissingRequiredField(null), Is.True);
        }

        [Test]
        [Description("HasMissingRequiredField は RootVisualTreeAsset 未設定の空プロファイルを true と判定する")]
        public void HasMissingRequiredField_EmptyProfile_IsTrue()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            try
            {
                Assert.That(SkinProfileEditor.HasMissingRequiredField(profile), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("HasMissingRequiredField は RootVisualTreeAsset が埋まっていれば false")]
        public void HasMissingRequiredField_ProfileWithRoot_IsFalse()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            VisualTreeAsset? vta = null;
            try
            {
                vta = ScriptableObject.CreateInstance<VisualTreeAsset>();
                profile.RootVisualTreeAsset = vta;
                Assert.That(SkinProfileEditor.HasMissingRequiredField(profile), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                if (vta != null) Object.DestroyImmediate(vta);
            }
        }

        [Test]
        [Description("既定 UXML/USS のパッケージパスが実在する（ボタン押下時に欠落しない）")]
        public void DefaultAssetPaths_PointToExistingPackageAssets()
        {
            var rootUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                SkinProfileEditor.DefaultRootUxmlPath);
            Assert.That(rootUxml, Is.Not.Null,
                $"Default root UXML must exist at {SkinProfileEditor.DefaultRootUxmlPath}");

            var rootUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                SkinProfileEditor.DefaultRootUssPath);
            Assert.That(rootUss, Is.Not.Null,
                $"Default root USS must exist at {SkinProfileEditor.DefaultRootUssPath}");

            var emptyTab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                SkinProfileEditor.DefaultEmptyTabUxmlPath);
            Assert.That(emptyTab, Is.Not.Null,
                $"EmptyTabShell UXML must exist at {SkinProfileEditor.DefaultEmptyTabUxmlPath}");
        }

        [Test]
        [Description("CopyPackageDefaults が空フィールドを埋め、Validate が成功するようになる")]
        public void CopyPackageDefaults_FillsEmptyFields_AndClearsMissingRoot()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            try
            {
                Assume.That(SkinProfileEditor.HasMissingRequiredField(profile), Is.True,
                    "Pre-condition: profile starts in 'missing required field' state");

                var written = SkinProfileEditor.CopyPackageDefaults(profile);

                Assert.That(written, Is.GreaterThan(0),
                    "At least one field should have been filled from package defaults");
                Assert.That(profile.RootVisualTreeAsset, Is.Not.Null,
                    "RootVisualTreeAsset must be filled by CopyPackageDefaults");
                Assert.That(SkinProfileEditor.HasMissingRequiredField(profile), Is.False,
                    "After copy the warning trigger must clear");

                Assert.That(profile.CharacterTabVisualTreeAsset, Is.Not.Null);
                Assert.That(profile.StageLightingTabVisualTreeAsset, Is.Not.Null);
                Assert.That(profile.CameraSwitcherTabVisualTreeAsset, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("CopyPackageDefaults は既存の手動設定を上書きしない（idempotent / non-destructive）")]
        public void CopyPackageDefaults_PreservesExistingFields()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            VisualTreeAsset? customRoot = null;
            VisualTreeAsset? customCharacter = null;
            try
            {
                customRoot = ScriptableObject.CreateInstance<VisualTreeAsset>();
                customCharacter = ScriptableObject.CreateInstance<VisualTreeAsset>();
                profile.RootVisualTreeAsset = customRoot;
                profile.CharacterTabVisualTreeAsset = customCharacter;

                SkinProfileEditor.CopyPackageDefaults(profile);

                Assert.That(profile.RootVisualTreeAsset, Is.SameAs(customRoot),
                    "Existing RootVisualTreeAsset must not be overwritten");
                Assert.That(profile.CharacterTabVisualTreeAsset, Is.SameAs(customCharacter),
                    "Existing CharacterTabVisualTreeAsset must not be overwritten");

                // 二度呼んでも追加書込みが起きないこと
                var secondPassWritten = SkinProfileEditor.CopyPackageDefaults(profile);
                Assert.That(secondPassWritten, Is.EqualTo(0),
                    "Second invocation must be a no-op when all fields are populated");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                if (customRoot != null) Object.DestroyImmediate(customRoot);
                if (customCharacter != null) Object.DestroyImmediate(customCharacter);
            }
        }

        [Test]
        [Description("CopyPackageDefaults は null プロファイルに対して 0 を返し例外を投げない")]
        public void CopyPackageDefaults_NullProfile_ReturnsZero()
        {
            Assert.DoesNotThrow(() =>
            {
                var written = SkinProfileEditor.CopyPackageDefaults(null!);
                Assert.That(written, Is.EqualTo(0));
            });
        }

        [Test]
        [Description("Inspector の主要文言（イントロ / 警告 / ボタン）は非空で利用者ガイドとして使える")]
        public void GuidanceCopy_IsPresent()
        {
            Assert.That(SkinProfileEditor.IntroHelp, Is.Not.Null.And.Not.Empty);
            Assert.That(SkinProfileEditor.MissingRootWarning, Is.Not.Null.And.Not.Empty);
            Assert.That(SkinProfileEditor.CopyDefaultsButtonLabel, Is.Not.Null.And.Not.Empty);
            Assert.That(SkinProfileEditor.MissingRootWarning,
                Does.Contain("RootVisualTreeAsset"),
                "Warning message must name the field the user has to fill");
            Assert.That(SkinProfileEditor.MissingRootWarning,
                Does.Contain(nameof(BootstrapErrorCode.SkinProfileMissing)),
                "Warning message must explain the resulting bootstrap error code");
        }
    }
}
