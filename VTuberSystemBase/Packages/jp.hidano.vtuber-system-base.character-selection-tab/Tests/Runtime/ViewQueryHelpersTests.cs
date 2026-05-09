#nullable enable
using NUnit.Framework;
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.View;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 4.1 acceptance: ViewQueryHelpers must surface required regions or
    /// throw a descriptive error when the UXML diverges.
    /// </summary>
    [TestFixture]
    public sealed class ViewQueryHelpersTests
    {
        private static VisualElement BuildRoot()
        {
            var root = new VisualElement { name = ViewQueryHelpers.TabRootName };
            root.Add(new VisualElement { name = ViewQueryHelpers.PresetBarRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.PlayerCardsRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.AvatarCatalogRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.SettingsPanelRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.DiagnosticsRegion });
            return root;
        }

        [Test]
        public void RequireByName_FindsAllExpectedRegions()
        {
            var root = BuildRoot();
            Assert.AreEqual(ViewQueryHelpers.PresetBarRegion,
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.PresetBarRegion).name);
            Assert.AreEqual(ViewQueryHelpers.PlayerCardsRegion,
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.PlayerCardsRegion).name);
            Assert.AreEqual(ViewQueryHelpers.AvatarCatalogRegion,
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.AvatarCatalogRegion).name);
            Assert.AreEqual(ViewQueryHelpers.SettingsPanelRegion,
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.SettingsPanelRegion).name);
            Assert.AreEqual(ViewQueryHelpers.DiagnosticsRegion,
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.DiagnosticsRegion).name);
        }

        [Test]
        public void RequireByName_ThrowsWhenMissing()
        {
            var root = new VisualElement { name = "empty" };
            Assert.Throws<InvalidOperationException>(() =>
                ViewQueryHelpers.RequireByName(root, ViewQueryHelpers.PlayerCardsRegion));
        }

        [Test]
        public void FindByName_ReturnsNullWhenMissing()
        {
            var root = new VisualElement { name = "empty" };
            Assert.IsNull(ViewQueryHelpers.FindByName(root, "no-such-region"));
        }
    }
}
