using NUnit.Framework;
using VTuberSystemBase.RacMainOutputAdapter.Domain;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class AvatarKeyValidatorTests
    {
        [Test]
        public void Null_IsInvalid()
        {
            Assert.That(AvatarKeyValidator.Validate(null), Is.False);
        }

        [Test]
        public void Empty_IsInvalid()
        {
            Assert.That(AvatarKeyValidator.Validate(""), Is.False);
        }

        [Test]
        public void Alphanumeric_IsValid()
        {
            Assert.That(AvatarKeyValidator.Validate("miku01"), Is.True);
            Assert.That(AvatarKeyValidator.Validate("Avatar_A"), Is.True);
            Assert.That(AvatarKeyValidator.Validate("avatar.v2"), Is.True);
            Assert.That(AvatarKeyValidator.Validate("a-b_c.d"), Is.True);
        }

        [Test]
        public void NonAsciiOrSpecial_IsInvalid()
        {
            Assert.That(AvatarKeyValidator.Validate("miku ku"), Is.False);   // space
            Assert.That(AvatarKeyValidator.Validate("miku/01"), Is.False);   // slash
            Assert.That(AvatarKeyValidator.Validate("ミク"), Is.False);      // non-ascii
            Assert.That(AvatarKeyValidator.Validate("miku@home"), Is.False); // @
        }
    }
}
