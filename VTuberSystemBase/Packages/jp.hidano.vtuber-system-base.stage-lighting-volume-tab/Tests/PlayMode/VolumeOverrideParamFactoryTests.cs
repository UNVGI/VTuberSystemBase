#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.StageLightingVolumeTab.View;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// Locks the ParamKind → VisualElement mapping (Task 6.5, Requirements 6.2, 6.10).
    /// Vector / Color controls are composites built from FloatField rows because
    /// Unity's Vector*Field / ColorField live in <c>UnityEditor.UIElements</c> and are
    /// not available in player-side runtime.
    /// </summary>
    [TestFixture]
    public sealed class VolumeOverrideParamFactoryTests
    {
        private static VolumeOverrideParamDto Param(ParamKind kind, VolumeOverrideParamValueDto def, VolumeOverrideParamRangeDto? range = null)
        {
            return new VolumeOverrideParamDto("p", kind, "P", def, range);
        }

        [Test]
        public void Bool_ReturnsToggle()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Bool, new VolumeOverrideParamValueDto(ParamKind.Bool, true, null, null, null, null, null)),
                new VolumeOverrideParamValueDto(ParamKind.Bool, true, null, null, null, null, null),
                _ => { });
            Assert.That(v, Is.InstanceOf<Toggle>());
        }

        [Test]
        public void Float_ReturnsFloatField_AndPropagatesChanges()
        {
            var factory = new VolumeOverrideParamFactory();
            VolumeOverrideParamValueDto? captured = null;
            var v = factory.CreateControl(
                Param(ParamKind.Float, new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 0f, null, null, null)),
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1.5f, null, null, null),
                val => captured = val);
            Assert.That(v, Is.InstanceOf<FloatField>());
            ((FloatField)v!).value = 5f;
            Assert.That(captured.HasValue, Is.True);
            Assert.That(captured!.Value.FloatValue, Is.EqualTo(5f));
        }

        [Test]
        public void ClampedFloat_ReturnsFloatField_AndUsesClampedFloatKind()
        {
            var factory = new VolumeOverrideParamFactory();
            VolumeOverrideParamValueDto? captured = null;
            var v = factory.CreateControl(
                Param(ParamKind.ClampedFloat, new VolumeOverrideParamValueDto(ParamKind.ClampedFloat, null, null, 0f, null, null, null)),
                new VolumeOverrideParamValueDto(ParamKind.ClampedFloat, null, null, 0.5f, null, null, null),
                val => captured = val);
            Assert.That(v, Is.InstanceOf<FloatField>());
            ((FloatField)v!).value = 0.7f;
            Assert.That(captured!.Value.Kind, Is.EqualTo(ParamKind.ClampedFloat));
        }

        [Test]
        public void Int_ReturnsIntegerField()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Int, new VolumeOverrideParamValueDto(ParamKind.Int, null, 0, null, null, null, null)),
                new VolumeOverrideParamValueDto(ParamKind.Int, null, 3, null, null, null, null),
                _ => { });
            Assert.That(v, Is.InstanceOf<IntegerField>());
        }

        [Test]
        public void Color_ReturnsCompositeContainerWithFourFloatChannels()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Color, new VolumeOverrideParamValueDto(ParamKind.Color, null, null, null, new ColorDto(1, 1, 1, 1), null, null)),
                new VolumeOverrideParamValueDto(ParamKind.Color, null, null, null, new ColorDto(1, 0.5f, 0.25f, 1f), null, null),
                _ => { });
            Assert.That(v, Is.Not.Null);
            int floatFieldCount = 0;
            foreach (var child in v!.Children())
            {
                if (child is FloatField) floatFieldCount++;
            }
            Assert.That(floatFieldCount, Is.EqualTo(4), "Color composite must expose R/G/B/A channels");
        }

        [Test]
        public void Vector2_ReturnsCompositeWithTwoChannels()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Vector2, new VolumeOverrideParamValueDto(ParamKind.Vector2, null, null, null, null, new Vector4Dto(0, 0, 0, 0), null)),
                new VolumeOverrideParamValueDto(ParamKind.Vector2, null, null, null, null, new Vector4Dto(1, 2, 0, 0), null),
                _ => { });
            int n = 0; foreach (var c in v!.Children()) if (c is FloatField) n++;
            Assert.That(n, Is.EqualTo(2));
        }

        [Test]
        public void Vector3_ReturnsCompositeWithThreeChannels()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Vector3, new VolumeOverrideParamValueDto(ParamKind.Vector3, null, null, null, null, new Vector4Dto(0, 0, 0, 0), null)),
                new VolumeOverrideParamValueDto(ParamKind.Vector3, null, null, null, null, new Vector4Dto(1, 2, 3, 0), null),
                _ => { });
            int n = 0; foreach (var c in v!.Children()) if (c is FloatField) n++;
            Assert.That(n, Is.EqualTo(3));
        }

        [Test]
        public void Vector4_ReturnsCompositeWithFourChannels()
        {
            var factory = new VolumeOverrideParamFactory();
            var v = factory.CreateControl(
                Param(ParamKind.Vector4, new VolumeOverrideParamValueDto(ParamKind.Vector4, null, null, null, null, new Vector4Dto(0, 0, 0, 0), null)),
                new VolumeOverrideParamValueDto(ParamKind.Vector4, null, null, null, null, new Vector4Dto(1, 2, 3, 4), null),
                _ => { });
            int n = 0; foreach (var c in v!.Children()) if (c is FloatField) n++;
            Assert.That(n, Is.EqualTo(4));
        }

        [Test]
        public void Enum_ReturnsDropdownFieldWithChoices()
        {
            var factory = new VolumeOverrideParamFactory();
            var range = new VolumeOverrideParamRangeDto(null, null, null, null,
                new List<string> { "A", "B", "C" });
            var v = factory.CreateControl(
                Param(ParamKind.Enum, new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "A"), range),
                new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "B"),
                _ => { });
            Assert.That(v, Is.InstanceOf<DropdownField>());
            var dd = (DropdownField)v!;
            Assert.That(dd.choices, Has.Count.EqualTo(3));
        }

        [Test]
        public void Unknown_ReturnsNull_AndLogs()
        {
            var logger = new FakeDiagnosticsLogger();
            var factory = new VolumeOverrideParamFactory(logger);
            var v = factory.CreateControl(
                Param(ParamKind.Unknown, new VolumeOverrideParamValueDto(ParamKind.Unknown, null, null, null, null, null, null)),
                new VolumeOverrideParamValueDto(ParamKind.Unknown, null, null, null, null, null, null),
                _ => { });
            Assert.That(v, Is.Null);
            Assert.That(logger.Entries, Is.Not.Empty);
        }
    }
}
