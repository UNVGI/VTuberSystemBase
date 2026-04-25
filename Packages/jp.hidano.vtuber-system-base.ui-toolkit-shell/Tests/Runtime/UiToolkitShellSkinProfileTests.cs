#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 6.1: <see cref="UiToolkitShellSkinProfile"/> contract tests. Pin the
    /// field layout (root + 3 tabs + common UI), verify the
    /// <see cref="CreateAssetMenuAttribute"/> path so the asset is reachable from
    /// <c>Assets &gt; Create</c>, and lock the empty-profile validation that returns
    /// <see cref="BootstrapErrorCode.SkinProfileMissing"/> (design.md §Skin
    /// §UiToolkitShellSkinProfile; Requirement 6.3, 6.4, 6.7, 6.8).
    /// </summary>
    [TestFixture]
    public sealed class UiToolkitShellSkinProfileTests
    {
        [Test]
        [Description("ScriptableObject として CreateInstance で生成できる（Editor メニュー UX の前提）")]
        public void Profile_CanBeCreated_AsScriptableObject()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            try
            {
                Assert.That(profile, Is.Not.Null);
                Assert.That(profile, Is.InstanceOf<ScriptableObject>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("CreateAssetMenu 属性が \"VTuberSystemBase/UI Toolkit Shell/Skin Profile\" で付与されている（Editor メニューから SO を生成可能）")]
        public void Profile_HasCreateAssetMenuAttribute_WithExpectedMenuName()
        {
            var attr = typeof(UiToolkitShellSkinProfile)
                .GetCustomAttributes(typeof(CreateAssetMenuAttribute), inherit: false)
                .OfType<CreateAssetMenuAttribute>()
                .FirstOrDefault();

            Assert.That(attr, Is.Not.Null,
                "UiToolkitShellSkinProfile must declare [CreateAssetMenu]");
            Assert.That(attr!.menuName,
                Is.EqualTo("VTuberSystemBase/UI Toolkit Shell/Skin Profile"));
            Assert.That(UiToolkitShellSkinProfile.CreateAssetMenuName,
                Is.EqualTo(attr.menuName),
                "Public constant must match the attribute argument so callers have a single source of truth");
        }

        [Test]
        [Description("Root + 3 タブ + 共通 UI のフィールドが揃っている（design.md §Skin の State Management に固定）")]
        public void Profile_ExposesRootAndAllTabAndCommonFields_WithExpectedTypes()
        {
            var t = typeof(UiToolkitShellSkinProfile);

            AssertFieldType(t, "RootVisualTreeAsset", typeof(VisualTreeAsset));
            AssertFieldType(t, "RootStyleSheets", typeof(List<StyleSheet>));

            AssertFieldType(t, "CharacterTabVisualTreeAsset", typeof(VisualTreeAsset));
            AssertFieldType(t, "CharacterTabStyleSheets", typeof(List<StyleSheet>));

            AssertFieldType(t, "StageLightingTabVisualTreeAsset", typeof(VisualTreeAsset));
            AssertFieldType(t, "StageLightingTabStyleSheets", typeof(List<StyleSheet>));

            AssertFieldType(t, "CameraSwitcherTabVisualTreeAsset", typeof(VisualTreeAsset));
            AssertFieldType(t, "CameraSwitcherTabStyleSheets", typeof(List<StyleSheet>));

            AssertFieldType(t, "CommonUiStyleSheets", typeof(List<StyleSheet>));
        }

        [Test]
        [Description("StyleSheet の List 群は new された空リストで初期化されている（null 参照解除エラーの予防）")]
        public void Profile_StyleSheetLists_AreInitializedEmpty_OnFreshInstance()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            try
            {
                Assert.That(profile.RootStyleSheets, Is.Not.Null);
                Assert.That(profile.RootStyleSheets, Is.Empty);

                Assert.That(profile.CharacterTabStyleSheets, Is.Not.Null);
                Assert.That(profile.CharacterTabStyleSheets, Is.Empty);

                Assert.That(profile.StageLightingTabStyleSheets, Is.Not.Null);
                Assert.That(profile.StageLightingTabStyleSheets, Is.Empty);

                Assert.That(profile.CameraSwitcherTabStyleSheets, Is.Not.Null);
                Assert.That(profile.CameraSwitcherTabStyleSheets, Is.Empty);

                Assert.That(profile.CommonUiStyleSheets, Is.Not.Null);
                Assert.That(profile.CommonUiStyleSheets, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("Validate(null) は SkinProfileMissing を返す（UiShellConfig.SkinProfile 未設定ケース）")]
        public void Validate_NullProfile_ReturnsSkinProfileMissing()
        {
            var error = UiToolkitShellSkinProfile.Validate(null);
            Assert.That(error.HasValue, Is.True);
            Assert.That(error!.Value, Is.EqualTo(BootstrapErrorCode.SkinProfileMissing));
        }

        [Test]
        [Description("空プロファイル（RootVisualTreeAsset == null）に対して Validate は SkinProfileMissing を返す（task 6.1 観測可能な完了状態）")]
        public void Validate_EmptyProfile_ReturnsSkinProfileMissing()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            try
            {
                var error = UiToolkitShellSkinProfile.Validate(profile);
                Assert.That(error.HasValue, Is.True);
                Assert.That(error!.Value, Is.EqualTo(BootstrapErrorCode.SkinProfileMissing));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        [Description("RootVisualTreeAsset が埋まったプロファイルは Validate で null（=エラーなし）になる")]
        public void Validate_ProfileWithRootVisualTreeAsset_ReturnsNull()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            VisualTreeAsset? vta = null;
            try
            {
                vta = ScriptableObject.CreateInstance<VisualTreeAsset>();
                profile.RootVisualTreeAsset = vta;

                var error = UiToolkitShellSkinProfile.Validate(profile);
                Assert.That(error, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                if (vta != null)
                {
                    UnityEngine.Object.DestroyImmediate(vta);
                }
            }
        }

        [Test]
        [Description("Validate で root が埋まっていればタブ UXML が null でも success（タブ未統合時は EmptyTabShell.uxml にフォールバック想定）")]
        public void Validate_ProfileWithOnlyRoot_AndAllTabsNull_ReturnsNull()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            VisualTreeAsset? vta = null;
            try
            {
                vta = ScriptableObject.CreateInstance<VisualTreeAsset>();
                profile.RootVisualTreeAsset = vta;
                Assert.That(profile.CharacterTabVisualTreeAsset, Is.Null);
                Assert.That(profile.StageLightingTabVisualTreeAsset, Is.Null);
                Assert.That(profile.CameraSwitcherTabVisualTreeAsset, Is.Null);

                var error = UiToolkitShellSkinProfile.Validate(profile);
                Assert.That(error, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                if (vta != null)
                {
                    UnityEngine.Object.DestroyImmediate(vta);
                }
            }
        }

        private static void AssertFieldType(Type type, string fieldName, Type expectedType)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.That(field, Is.Not.Null,
                $"UiToolkitShellSkinProfile must expose public field '{fieldName}'");
            Assert.That(field!.FieldType, Is.EqualTo(expectedType),
                $"Field '{fieldName}' must be of type {expectedType.FullName}");
        }
    }
}
