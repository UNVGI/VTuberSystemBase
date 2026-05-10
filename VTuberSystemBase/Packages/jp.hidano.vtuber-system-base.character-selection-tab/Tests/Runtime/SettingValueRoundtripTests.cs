#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 1.2 acceptance test: each <see cref="SettingValue"/> kind survives a
    /// JSON roundtrip (constructor → ToJson → FromJson) without loss. Covered: Float,
    /// Int, Bool, Color, Enum, Vector3.
    /// </summary>
    [TestFixture]
    public sealed class SettingValueRoundtripTests
    {
        [Test]
        public void Float_Roundtrips()
        {
            var v = SettingValue.Float(0.625f);
            var json = v.ToJson();
            var back = SettingValue.FromJson(SettingType.Float, json);
            Assert.AreEqual(SettingType.Float, back.Type);
            Assert.AreEqual(0.625f, back.FloatValue);
        }

        [Test]
        public void Int_Roundtrips()
        {
            var v = SettingValue.Int(-42);
            var json = v.ToJson();
            var back = SettingValue.FromJson(SettingType.Int, json);
            Assert.AreEqual(SettingType.Int, back.Type);
            Assert.AreEqual(-42, back.IntValue);
        }

        [Test]
        public void Bool_Roundtrips()
        {
            var trueRoundtrip = SettingValue.FromJson(SettingType.Bool, SettingValue.Bool(true).ToJson());
            var falseRoundtrip = SettingValue.FromJson(SettingType.Bool, SettingValue.Bool(false).ToJson());
            Assert.IsTrue(trueRoundtrip.BoolValue);
            Assert.IsFalse(falseRoundtrip.BoolValue);
        }

        [Test]
        public void Color_Roundtrips()
        {
            var c = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            var v = SettingValue.Color(c);
            var back = SettingValue.FromJson(SettingType.Color, v.ToJson());
            Assert.AreEqual(c.r, back.ColorValue.r, 1e-5f);
            Assert.AreEqual(c.g, back.ColorValue.g, 1e-5f);
            Assert.AreEqual(c.b, back.ColorValue.b, 1e-5f);
            Assert.AreEqual(c.a, back.ColorValue.a, 1e-5f);
        }

        [Test]
        public void Enum_Roundtrips()
        {
            var v = SettingValue.Enum("happy");
            var back = SettingValue.FromJson(SettingType.Enum, v.ToJson());
            Assert.AreEqual("happy", back.EnumValue);
        }

        [Test]
        public void Vector3_Roundtrips()
        {
            var vec = new Vector3(1.0f, 2.0f, 3.0f);
            var v = SettingValue.Vector3(vec);
            var back = SettingValue.FromJson(SettingType.Vector3, v.ToJson());
            Assert.AreEqual(vec, back.Vector3Value);
        }

        [Test]
        public void Equality_DistinguishesTypes()
        {
            Assert.AreNotEqual(SettingValue.Float(1f), SettingValue.Int(1));
            Assert.AreEqual(SettingValue.Float(1f), SettingValue.Float(1f));
        }
    }
}
