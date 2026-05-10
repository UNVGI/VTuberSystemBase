#nullable enable
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the JSON wire format for the Preview command/state DTOs (Task 2.4,
    /// Requirements 2.6, 2.7, 2.8).
    /// </summary>
    [TestFixture]
    public sealed class PreviewDtosJsonRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        [Test]
        public void PreviewCommandDto_SetEnabledTrue_RoundtripsWithEnabledPopulated()
        {
            var v = new PreviewCommandDto("set-enabled", true);
            var rt = JsonSerializer.Deserialize<PreviewCommandDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.Op, Is.EqualTo("set-enabled"));
            Assert.That(rt.Enabled, Is.True);
        }

        [Test]
        public void PreviewCommandDto_SetEnabledFalse_RoundtripsWithEnabledPopulated()
        {
            var v = new PreviewCommandDto("set-enabled", false);
            var rt = JsonSerializer.Deserialize<PreviewCommandDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.Enabled, Is.False);
        }

        [Test]
        public void PreviewCommandDto_ResetView_HasNullEnabled()
        {
            var v = new PreviewCommandDto("reset-view", null);
            var rt = JsonSerializer.Deserialize<PreviewCommandDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.Op, Is.EqualTo("reset-view"));
            Assert.That(rt.Enabled, Is.Null);
        }

        [Test]
        public void PreviewCommandDto_InitAndDispose_RoundtripsThroughJson()
        {
            foreach (var op in new[] { "init", "dispose" })
            {
                var v = new PreviewCommandDto(op, null);
                var rt = JsonSerializer.Deserialize<PreviewCommandDto>(
                    JsonSerializer.Serialize(v, Options), Options);
                Assert.That(rt.Op, Is.EqualTo(op));
            }
        }

        [Test]
        public void PreviewStateDto_RoundtripsThroughJson()
        {
            foreach (var enabled in new[] { true, false })
            foreach (var ready in new[] { true, false })
            {
                var v = new PreviewStateDto(enabled, ready);
                var rt = JsonSerializer.Deserialize<PreviewStateDto>(
                    JsonSerializer.Serialize(v, Options), Options);
                Assert.That(rt.Enabled, Is.EqualTo(enabled));
                Assert.That(rt.HostReady, Is.EqualTo(ready));
            }
        }
    }
}
