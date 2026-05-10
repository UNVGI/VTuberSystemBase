#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.CommonUi;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 7.5: <see cref="CommonUiRegistration"/> contract tests.
    /// <para>
    /// 4 コントロール（<see cref="VsbSlider"/>, <see cref="VsbColorPicker"/>,
    /// <see cref="VsbNumberedList"/>, <see cref="VsbToggleGroup"/>）の
    /// <c>UxmlFactory</c> と既定 USS を <c>UiShellBootstrapper</c> 初期化時に
    /// 1 度だけ一括登録する <see cref="CommonUiRegistration.RegisterAll"/> の
    /// 観測可能契約を pin する（design.md §CommonUi §CommonUiRegistration;
    /// Requirement 7.2, 7.5）。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class CommonUiRegistrationTests
    {
        private const string PackagePath =
            "Packages/com.hidano.vtuber-system-base.ui-toolkit-shell";

        private const string SliderUxmlPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbSlider.uxml";

        private const string ColorPickerUxmlPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbColorPicker.uxml";

        private const string NumberedListUxmlPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbNumberedList.uxml";

        private const string ToggleGroupUxmlPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbToggleGroup.uxml";

        private const string SliderUssPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbSlider.uss";

        private const string ColorPickerUssPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbColorPicker.uss";

        private const string NumberedListUssPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbNumberedList.uss";

        private const string ToggleGroupUssPath =
            PackagePath + "/Runtime.CommonUi/Controls/VsbToggleGroup.uss";

        // ---------- RegisterAll() basic contract ----------

        [Test]
        [Description("RegisterAll() は冪等で、複数回呼び出しても例外を投げない（UiShellBootstrapper の起動時 1 回呼出契約の安全網）")]
        public void RegisterAll_IsIdempotent_DoesNotThrowOnRepeatedCalls()
        {
            Assert.DoesNotThrow(() => CommonUiRegistration.RegisterAll());
            Assert.DoesNotThrow(() => CommonUiRegistration.RegisterAll());
            Assert.DoesNotThrow(() => CommonUiRegistration.RegisterAll());
        }

        [Test]
        [Description("RegisterAll() 呼出後に IsRegistered が true になる（観測可能な完了状態）")]
        public void RegisterAll_SetsIsRegisteredFlag()
        {
            CommonUiRegistration.RegisterAll();

            Assert.That(CommonUiRegistration.IsRegistered, Is.True);
        }

        // ---------- Registered control types ----------

        [Test]
        [Description("RegisteredControlTypes は VsbSlider / VsbColorPicker / VsbNumberedList / VsbToggleGroup の 4 件をちょうど含む")]
        public void RegisteredControlTypes_ContainsAllFourControls()
        {
            var types = new HashSet<Type>(CommonUiRegistration.RegisteredControlTypes);

            Assert.That(types.Count, Is.EqualTo(4),
                "登録対象は task 7.1〜7.4 の 4 コントロールに固定（design.md §CommonUi）");
            Assert.That(types, Does.Contain(typeof(VsbSlider)));
            Assert.That(types, Does.Contain(typeof(VsbColorPicker)));
            Assert.That(types, Does.Contain(typeof(VsbNumberedList)));
            Assert.That(types, Does.Contain(typeof(VsbToggleGroup)));
        }

        // ---------- Default USS asset paths ----------

        [Test]
        [Description("DefaultStyleSheetAssetPaths は 4 コントロールの既定 USS を漏れなく列挙する")]
        public void DefaultStyleSheetAssetPaths_ListsAllFourDefaultUssFiles()
        {
            var paths = new HashSet<string>(CommonUiRegistration.DefaultStyleSheetAssetPaths, StringComparer.Ordinal);

            Assert.That(paths.Count, Is.EqualTo(4),
                "既定 USS は task 7.1〜7.4 の 4 ファイル分のみ（重複・欠落なし）");
            Assert.That(paths, Does.Contain(SliderUssPath));
            Assert.That(paths, Does.Contain(ColorPickerUssPath));
            Assert.That(paths, Does.Contain(NumberedListUssPath));
            Assert.That(paths, Does.Contain(ToggleGroupUssPath));
        }

        [Test]
        [Description("DefaultStyleSheetAssetPaths が指すファイルはすべて StyleSheet として AssetDatabase からロード可能（パッケージ同梱の確認）")]
        public void DefaultStyleSheetAssetPaths_AllLoadAsStyleSheet()
        {
            foreach (var path in CommonUiRegistration.DefaultStyleSheetAssetPaths)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                Assert.That(sheet, Is.Not.Null,
                    $"既定 USS '{path}' を StyleSheet として読めない（パッケージ同梱の確認）");
            }
        }

        // ---------- UXML reference resolution (post-RegisterAll) ----------

        [Test]
        [Description("RegisterAll 後、UXML 参照 <vsb:VsbSlider /> が VsbSlider インスタンスに解決される（Requirement 7.2）")]
        public void AfterRegisterAll_VsbSliderUxmlReferenceResolves()
        {
            CommonUiRegistration.RegisterAll();

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SliderUxmlPath);
            Assume.That(vta, Is.Not.Null, $"既定 UXML '{SliderUxmlPath}' をロードできない");

            var root = vta.Instantiate();
            var slider = root.Q<VsbSlider>();

            Assert.That(slider, Is.Not.Null, "UXML 参照の <vsb:VsbSlider/> が VsbSlider に解決されていない");
            Assert.That(slider!.ClassListContains("vsb-slider"), Is.True);
        }

        [Test]
        [Description("RegisterAll 後、UXML 参照 <vsb:VsbColorPicker /> が VsbColorPicker インスタンスに解決される（Requirement 7.2）")]
        public void AfterRegisterAll_VsbColorPickerUxmlReferenceResolves()
        {
            CommonUiRegistration.RegisterAll();

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ColorPickerUxmlPath);
            Assume.That(vta, Is.Not.Null, $"既定 UXML '{ColorPickerUxmlPath}' をロードできない");

            var root = vta.Instantiate();
            var picker = root.Q<VsbColorPicker>();

            Assert.That(picker, Is.Not.Null, "UXML 参照の <vsb:VsbColorPicker/> が VsbColorPicker に解決されていない");
            Assert.That(picker!.ClassListContains("vsb-color-picker"), Is.True);
        }

        [Test]
        [Description("RegisterAll 後、UXML 参照 <vsb:VsbNumberedList /> が VsbNumberedList インスタンスに解決される（Requirement 7.2）")]
        public void AfterRegisterAll_VsbNumberedListUxmlReferenceResolves()
        {
            CommonUiRegistration.RegisterAll();

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(NumberedListUxmlPath);
            Assume.That(vta, Is.Not.Null, $"既定 UXML '{NumberedListUxmlPath}' をロードできない");

            var root = vta.Instantiate();
            var list = root.Q<VsbNumberedList>();

            Assert.That(list, Is.Not.Null, "UXML 参照の <vsb:VsbNumberedList/> が VsbNumberedList に解決されていない");
            Assert.That(list!.ClassListContains("vsb-numbered-list"), Is.True);
        }

        [Test]
        [Description("RegisterAll 後、UXML 参照 <vsb:VsbToggleGroup /> が VsbToggleGroup インスタンスに解決される（Requirement 7.2）")]
        public void AfterRegisterAll_VsbToggleGroupUxmlReferenceResolves()
        {
            CommonUiRegistration.RegisterAll();

            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ToggleGroupUxmlPath);
            Assume.That(vta, Is.Not.Null, $"既定 UXML '{ToggleGroupUxmlPath}' をロードできない");

            var root = vta.Instantiate();
            var group = root.Q<VsbToggleGroup>();

            Assert.That(group, Is.Not.Null, "UXML 参照の <vsb:VsbToggleGroup/> が VsbToggleGroup に解決されていない");
            Assert.That(group!.ClassListContains("vsb-toggle-group"), Is.True);
        }
    }
}
