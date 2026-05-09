using System;
using System.Text.Json;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Domain;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class SettingValueDecoderTests
    {
        private static JsonElement Parse(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        [Test]
        public void Float_DecodesNumber()
        {
            var v = SettingValueDecoder.Decode(SettingType.Float, Parse("1.5"));
            Assert.That(v, Is.TypeOf<float>().And.EqualTo(1.5f));
        }

        [Test]
        public void Int_DecodesInteger()
        {
            var v = SettingValueDecoder.Decode(SettingType.Int, Parse("42"));
            Assert.That(v, Is.TypeOf<int>().And.EqualTo(42));
        }

        [Test]
        public void Bool_DecodesTrueFalse()
        {
            Assert.That(SettingValueDecoder.Decode(SettingType.Bool, Parse("true")), Is.EqualTo(true));
            Assert.That(SettingValueDecoder.Decode(SettingType.Bool, Parse("false")), Is.EqualTo(false));
        }

        [Test]
        public void Color_DecodesArrayOf4()
        {
            var v = (Color)SettingValueDecoder.Decode(SettingType.Color, Parse("[1.0, 0.5, 0.0, 1.0]"));
            Assert.That(v.r, Is.EqualTo(1.0f));
            Assert.That(v.g, Is.EqualTo(0.5f));
            Assert.That(v.b, Is.EqualTo(0.0f));
            Assert.That(v.a, Is.EqualTo(1.0f));
        }

        [Test]
        public void Color_DecodesArrayOf3WithAlpha1()
        {
            var v = (Color)SettingValueDecoder.Decode(SettingType.Color, Parse("[0.2, 0.4, 0.6]"));
            Assert.That(v.a, Is.EqualTo(1f));
        }

        [Test]
        public void Enum_DecodesString()
        {
            var v = SettingValueDecoder.Decode(SettingType.Enum, Parse("\"Smile\""));
            Assert.That(v, Is.EqualTo("Smile"));
        }

        [Test]
        public void Vector3_DecodesArrayOf3()
        {
            var v = (Vector3)SettingValueDecoder.Decode(SettingType.Vector3, Parse("[1, 2, 3]"));
            Assert.That(v, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void Float_TypeMismatch_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SettingValueDecoder.Decode(SettingType.Float, Parse("\"text\"")));
        }

        [Test]
        public void Bool_TypeMismatch_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SettingValueDecoder.Decode(SettingType.Bool, Parse("1")));
        }

        [Test]
        public void Color_WrongLength_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SettingValueDecoder.Decode(SettingType.Color, Parse("[1, 2]")));
        }

        [Test]
        public void Vector3_WrongLength_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SettingValueDecoder.Decode(SettingType.Vector3, Parse("[1, 2]")));
        }

        [Test]
        public void UnknownType_ReturnsNull()
        {
            var unknown = (SettingType)9999;
            Assert.That(SettingValueDecoder.Decode(unknown, Parse("1")), Is.Null);
        }
    }
}
