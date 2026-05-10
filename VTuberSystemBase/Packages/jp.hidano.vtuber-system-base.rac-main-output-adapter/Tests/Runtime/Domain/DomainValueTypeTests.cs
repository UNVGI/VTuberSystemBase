using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;
using VTuberSystemBase.RacMainOutputAdapter.Defaults;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class DomainValueTypeTests
    {
        [Test]
        public void AdapterApplyResult_HasFourMembers()
        {
            Assert.That((int)AdapterApplyResult.Applied, Is.EqualTo(0));
            Assert.That((int)AdapterApplyResult.UnknownKey, Is.EqualTo(1));
            Assert.That((int)AdapterApplyResult.OutOfRange, Is.EqualTo(2));
            Assert.That((int)AdapterApplyResult.Failed, Is.EqualTo(3));
        }

        [Test]
        public void PendingSettingValue_RecordEqualityWorks()
        {
            using var doc1 = JsonDocument.Parse("1.5");
            using var doc2 = JsonDocument.Parse("1.5");

            var a = new PendingSettingValue("k", SettingType.Float, doc1.RootElement, 100L);
            var b = new PendingSettingValue("k", SettingType.Float, doc1.RootElement, 100L);
            var c = new PendingSettingValue("k2", SettingType.Float, doc1.RootElement, 100L);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void RacMainOutputAdapterConfig_DefaultsAreSpecified()
        {
            var c = new RacMainOutputAdapterConfig();
            Assert.That(c.SchemaProviderSlowThresholdMs, Is.EqualTo(4000));
            Assert.That(c.MaxErrorDetailLength, Is.EqualTo(512));
            Assert.That(c.PendingPublishQueueCapacity, Is.EqualTo(16));
        }

        [Test]
        public void DefaultClock_ReturnsCurrentUtcTime()
        {
            var clock = new DefaultClock();
            var before = System.DateTimeOffset.UtcNow;
            var t = clock.UtcNow;
            var after = System.DateTimeOffset.UtcNow;
            Assert.That(t, Is.GreaterThanOrEqualTo(before));
            Assert.That(t, Is.LessThanOrEqualTo(after));
        }
    }
}
