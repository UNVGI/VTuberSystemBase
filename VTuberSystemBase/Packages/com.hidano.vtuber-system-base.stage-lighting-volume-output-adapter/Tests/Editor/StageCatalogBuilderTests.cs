#nullable enable
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class StageCatalogBuilderTests
    {
        [Test]
        public void Build_WithMultipleLocations_ReturnsMatchingDto()
        {
            var provider = new FakeInstantiationProvider();
            provider.ConfigureLabel("stage", new[] { "Stages/Default", "Stages/Studio" });
            var builder = new StageCatalogBuilder();
            var dto = builder.BuildAsync(provider).Result;
            Assert.That(dto.Items.Count, Is.EqualTo(2));
            Assert.That(dto.Items[0].AddressableKey, Is.EqualTo("Stages/Default"));
            Assert.That(dto.Items[0].DisplayName, Is.EqualTo("Stages/Default"));
            Assert.That(dto.Items[0].ThumbnailAddressableKey, Is.EqualTo("Stages/Default.thumbnail"));
            Assert.That(dto.Items[1].AddressableKey, Is.EqualTo("Stages/Studio"));
        }

        [Test]
        public void Build_WithEmptyLabel_ReturnsEmpty_AndLogsWarning()
        {
            var provider = new FakeInstantiationProvider(); // no label configured
            var logger = new AdapterLogger();
            var builder = new StageCatalogBuilder(logger);
            LogAssert.Expect(LogType.Warning, new Regex("StageCatalogBuilder.label_not_found"));
            var dto = builder.BuildAsync(provider).Result;
            Assert.That(dto.Items.Count, Is.EqualTo(0));
        }
    }
}
