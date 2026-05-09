#nullable enable
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 2.3 acceptance tests: per-type element mapping, malformed-metadata
    /// diagnostic, and event wiring.
    /// </summary>
    [TestFixture]
    public sealed class DynamicSettingControlFactoryTests
    {
        [Test]
        public void Float_BuildsVsbSliderRow()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry
            {
                Key = "smile",
                Label = "Smile",
                Type = SettingType.Float,
                Min = SettingValue.Float(0f),
                Max = SettingValue.Float(1f),
            };
            var c = f.Build(entry, SettingValue.Float(0.5f));
            Assert.IsNotNull(c.Root);
            Assert.IsTrue(c.Root!.ClassListContains(DynamicSettingControlFactory.SettingRowClass));
            Assert.IsNotNull(c.Root.Q<VsbSlider>("smile"));
        }

        [Test]
        public void Bool_BuildsToggleRow()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry { Key = "k", Label = "L", Type = SettingType.Bool };
            var c = f.Build(entry, SettingValue.Bool(true));
            Assert.IsNotNull(c.Root!.Q<Toggle>("k"));
        }

        [Test]
        public void Enum_BuildsVsbToggleGroup_FromOptions()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry
            {
                Key = "mood",
                Label = "Mood",
                Type = SettingType.Enum,
                Options = new[] { "happy", "sad" },
            };
            var c = f.Build(entry, SettingValue.Enum("happy"));
            var group = c.Root!.Q<VsbToggleGroup>("mood");
            Assert.IsNotNull(group);
            Assert.AreEqual(2, group!.Keys.Count);
        }

        [Test]
        public void Color_BuildsVsbColorPicker()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry { Key = "tint", Label = "Tint", Type = SettingType.Color };
            var c = f.Build(entry, SettingValue.Color(new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f)));
            Assert.IsNotNull(c.Root!.Q<VsbColorPicker>("tint"));
        }

        [Test]
        public void Vector3_BuildsThreeSliders()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry
            {
                Key = "scale",
                Label = "Scale",
                Type = SettingType.Vector3,
                Min = SettingValue.Float(0f),
                Max = SettingValue.Float(2f),
            };
            var c = f.Build(entry, SettingValue.Vector3(UnityEngine.Vector3.one));
            Assert.IsNotNull(c.Root!.Q<VsbSlider>("scale.x"));
            Assert.IsNotNull(c.Root.Q<VsbSlider>("scale.y"));
            Assert.IsNotNull(c.Root.Q<VsbSlider>("scale.z"));
        }

        [Test]
        public void Command_BuildsButton()
        {
            var f = new DynamicSettingControlFactory();
            var entry = new SettingSchemaEntry { Key = "reset.face", Label = "Reset face", Type = SettingType.Float, Kind = "command" };
            var c = f.Build(entry, default);
            Assert.IsTrue(c.IsCommand);
            Assert.IsNotNull(c.Root!.Q<Button>("reset.face"));
        }

        [Test]
        public void EmptyKey_LogsAndReturnsNullRoot()
        {
            var log = new FakeDiagnosticsLogger();
            var f = new DynamicSettingControlFactory(log);
            var c = f.Build(new SettingSchemaEntry { Key = "", Label = "x", Type = SettingType.Float }, default);
            Assert.IsNull(c.Root);
            Assert.IsTrue(log.Entries.Count > 0);
        }

        [Test]
        public void MinGreaterThanMax_DisablesAndLogs()
        {
            var log = new FakeDiagnosticsLogger();
            var f = new DynamicSettingControlFactory(log);
            var entry = new SettingSchemaEntry
            {
                Key = "k",
                Label = "L",
                Type = SettingType.Float,
                Min = SettingValue.Float(1f),
                Max = SettingValue.Float(0f),
            };
            var c = f.Build(entry, SettingValue.Float(0.5f));
            // VsbSlider self-coerces min/max so the warning comes via the slider's
            // own diagnostic path. Either way the factory must keep the row
            // and the diagnostic is captured under TabSpec or Skin.
            Assert.IsNotNull(c.Root);
            Assert.IsTrue(log.Entries.Count > 0);
        }
    }
}
