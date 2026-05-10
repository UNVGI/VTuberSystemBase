#nullable enable
using System.Collections;
using System.Text.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class VolumeParameterValueWriterTests
    {
        [UnityTest]
        public IEnumerator FloatParameter_RoundTripsValue()
        {
            yield return null;
            var bloom = ScriptableObject.CreateInstance<Bloom>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var jsonElement = JsonDocument.Parse("2.5").RootElement;
                var result = writer.Write(bloom, nameof(Bloom.intensity), jsonElement);
                Assert.That(result.Success, Is.True, result.Detail ?? "");
                Assert.That(bloom.intensity.value, Is.EqualTo(2.5f).Within(1e-3f));
                Assert.That(bloom.intensity.overrideState, Is.True);
            }
            finally { Object.Destroy(bloom); }
        }

        [UnityTest]
        public IEnumerator ColorParameter_AcceptsRgbaObject()
        {
            yield return null;
            var bloom = ScriptableObject.CreateInstance<Bloom>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var jsonElement = JsonDocument.Parse("{\"r\":0.5,\"g\":0.25,\"b\":0.75,\"a\":0.9}").RootElement;
                var result = writer.Write(bloom, nameof(Bloom.tint), jsonElement);
                Assert.That(result.Success, Is.True, result.Detail ?? "");
                Assert.That(bloom.tint.value.r, Is.EqualTo(0.5f).Within(1e-3f));
                Assert.That(bloom.tint.value.g, Is.EqualTo(0.25f).Within(1e-3f));
                Assert.That(bloom.tint.value.b, Is.EqualTo(0.75f).Within(1e-3f));
                Assert.That(bloom.tint.value.a, Is.EqualTo(0.9f).Within(1e-3f));
                Assert.That(bloom.tint.overrideState, Is.True);
            }
            finally { Object.Destroy(bloom); }
        }

        [UnityTest]
        public IEnumerator EnumParameter_AcceptsIntegerWireValue()
        {
            yield return null;
            var tonemapping = ScriptableObject.CreateInstance<Tonemapping>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var aces = (int)TonemappingMode.ACES;
                var jsonElement = JsonDocument.Parse(aces.ToString()).RootElement;
                var result = writer.Write(tonemapping, nameof(Tonemapping.mode), jsonElement);
                Assert.That(result.Success, Is.True, result.Detail ?? "");
                Assert.That(tonemapping.mode.value, Is.EqualTo(TonemappingMode.ACES));
                Assert.That(tonemapping.mode.overrideState, Is.True);
            }
            finally { Object.Destroy(tonemapping); }
        }

        [UnityTest]
        public IEnumerator BoolParameter_AcceptsJsonTrueFalse()
        {
            yield return null;
            var bloom = ScriptableObject.CreateInstance<Bloom>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var on = JsonDocument.Parse("true").RootElement;
                var result = writer.Write(bloom, nameof(Bloom.highQualityFiltering), on);
                Assert.That(result.Success, Is.True, result.Detail ?? "");
                Assert.That(bloom.highQualityFiltering.value, Is.True);
            }
            finally { Object.Destroy(bloom); }
        }

        [UnityTest]
        public IEnumerator UnknownParam_ReturnsParamNotFound()
        {
            yield return null;
            var bloom = ScriptableObject.CreateInstance<Bloom>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var json = JsonDocument.Parse("1").RootElement;
                var result = writer.Write(bloom, "totallyMadeUpField", json);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Reason, Is.EqualTo(VolumeBindFailureReasons.ParamNotFound));
            }
            finally { Object.Destroy(bloom); }
        }

        [UnityTest]
        public IEnumerator MismatchedJsonShape_ReturnsParamTypeMismatch()
        {
            yield return null;
            var bloom = ScriptableObject.CreateInstance<Bloom>();
            try
            {
                var writer = new VolumeParameterValueWriter();
                var json = JsonDocument.Parse("\"not-a-number\"").RootElement;
                var result = writer.Write(bloom, nameof(Bloom.intensity), json);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Reason, Is.EqualTo(VolumeBindFailureReasons.ParamTypeMismatch));
            }
            finally { Object.Destroy(bloom); }
        }
    }
}
