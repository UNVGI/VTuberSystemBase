using System.Text.Json;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Defaults;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Defaults
{
    [TestFixture]
    public sealed class NoOpAvatarSettingsAdapterTests
    {
        [Test]
        public void Apply_ReturnsUnknownKey_ForAnyInput()
        {
            var adapter = new NoOpAvatarSettingsAdapter();
            using var doc = JsonDocument.Parse("0.5");
            // Avatar GameObject は null でも UnknownKey を返す（NoOp なので副作用なし）
            var result = adapter.Apply(null, "anyKey", SettingType.Float, doc.RootElement);
            Assert.That(result, Is.EqualTo(AdapterApplyResult.UnknownKey));
        }

        [Test]
        public void Apply_ReturnsUnknownKey_EvenForKnownLookingKey()
        {
            var adapter = new NoOpAvatarSettingsAdapter();
            using var doc = JsonDocument.Parse("\"Smile\"");
            var result = adapter.Apply(null, "expression", SettingType.Enum, doc.RootElement);
            Assert.That(result, Is.EqualTo(AdapterApplyResult.UnknownKey));
        }
    }
}
