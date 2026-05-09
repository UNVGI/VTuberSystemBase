#nullable enable
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class LightPropertyApplierTests
    {
        private LightRegistry _registry = null!;
        private LightPropertyApplier _sut = null!;
        private GameObject _go = null!;
        private Light _light = null!;
        private LightEntry _entry = null!;

        [SetUp]
        public void SetUp()
        {
            _registry = new LightRegistry();
            _sut = new LightPropertyApplier(_registry, new AdapterLogger());
            _go = new GameObject("Light_id1");
            _light = _go.AddComponent<Light>();
            _light.type = LightType.Point;
            _entry = new LightEntry("id1", _go, _light,
                new LightInitialDto(LightTypeDto.Point, default, new ColorDto(1, 1, 1, 1), 1f, 10f, 30f, "L1"));
            _registry.Add("id1", _entry);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_go); }

        private static StateCommand<T> Cmd<T>(string topic, T payload) => new() { Topic = topic, Payload = payload };

        [Test]
        public void ApplyIntensity_SetsLightIntensity()
        {
            _sut.ApplyIntensity("id1", Cmd("light/id1/intensity", 2.5f));
            Assert.That(_light.intensity, Is.EqualTo(2.5f));
        }

        [Test]
        public void ApplyColor_SetsLightColor()
        {
            _sut.ApplyColor("id1", Cmd("light/id1/color", new ColorDto(0.1f, 0.2f, 0.3f, 0.4f)));
            Assert.That(_light.color.r, Is.EqualTo(0.1f).Within(1e-6));
            Assert.That(_light.color.a, Is.EqualTo(0.4f).Within(1e-6));
        }

        [Test]
        public void ApplyRotation_SetsLocalRotation()
        {
            _sut.ApplyRotation("id1", Cmd("light/id1/rotation", new Vector3Dto(45, 90, 0)));
            var expected = Quaternion.Euler(45, 90, 0);
            Assert.That(_go.transform.localRotation.x, Is.EqualTo(expected.x).Within(1e-5));
            Assert.That(_go.transform.localRotation.w, Is.EqualTo(expected.w).Within(1e-5));
        }

        [Test]
        public void ApplyType_SetsLightType()
        {
            _sut.ApplyType("id1", Cmd("light/id1/type", LightTypeDto.Spot));
            Assert.That(_light.type, Is.EqualTo(LightType.Spot));
        }

        [Test]
        public void ApplyRange_SetsLightRange()
        {
            _sut.ApplyRange("id1", Cmd("light/id1/range", 7.25f));
            Assert.That(_light.range, Is.EqualTo(7.25f));
        }

        [Test]
        public void ApplySpotAngle_SetsLightSpotAngle()
        {
            _sut.ApplySpotAngle("id1", Cmd("light/id1/spotAngle", 45f));
            Assert.That(_light.spotAngle, Is.EqualTo(45f));
        }

        [Test]
        public void ApplyDisplayName_UpdatesEntryName_NotGameObjectName()
        {
            _sut.ApplyDisplayName("id1", Cmd("light/id1/displayName", "New Name"));
            Assert.That(_entry.DisplayName, Is.EqualTo("New Name"));
            // GameObject name remains "Light_{id}".
            Assert.That(_go.name, Is.EqualTo("Light_id1"));
        }

        [Test]
        public void UnknownLightId_LogsWarning_AndDoesNotThrow()
        {
            LogAssert.Expect(LogType.Warning, new Regex("unknown_light_id"));
            Assert.DoesNotThrow(() => _sut.ApplyIntensity("missing", Cmd<float>("x", 1f)));
        }

        [Test]
        public void ApplyInitial_AppliesAllProperties()
        {
            var initial = new LightInitialDto(LightTypeDto.Spot, new Vector3Dto(0, 90, 0),
                new ColorDto(1, 0, 0, 1), 3f, 12f, 60f, "Spot1");
            _sut.ApplyInitial(_entry, initial);
            Assert.That(_light.type, Is.EqualTo(LightType.Spot));
            Assert.That(_light.intensity, Is.EqualTo(3f));
            Assert.That(_light.range, Is.EqualTo(12f));
            Assert.That(_light.spotAngle, Is.EqualTo(60f));
            Assert.That(_light.color.r, Is.EqualTo(1f).Within(1e-6));
            Assert.That(_entry.DisplayName, Is.EqualTo("Spot1"));
        }
    }
}
