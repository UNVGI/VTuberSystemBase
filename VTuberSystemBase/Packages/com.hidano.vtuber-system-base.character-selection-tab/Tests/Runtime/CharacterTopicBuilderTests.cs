#nullable enable
using NUnit.Framework;
using System;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 1.3 acceptance test: <see cref="CharacterTopics"/> percent-encodes
    /// non-safe characters and rejects null/empty inputs while keeping ASCII
    /// alphanumerics and <c>- _ .</c> intact.
    /// </summary>
    [TestFixture]
    public sealed class CharacterTopicBuilderTests
    {
        [Test]
        public void SlotsCatalog_IsConstant()
        {
            Assert.AreEqual("slots/catalog", CharacterTopics.SlotsCatalog);
        }

        [Test]
        public void AvatarsCatalog_IsConstant()
        {
            Assert.AreEqual("avatars/catalog", CharacterTopics.AvatarsCatalog);
        }

        [Test]
        public void SlotAssignment_NormalValueUntouched()
        {
            Assert.AreEqual("slot/slot-01/assignment", CharacterTopics.SlotAssignment("slot-01"));
        }

        [Test]
        public void SlotSettingValue_BothSegmentsEncoded()
        {
            // '/' is not safe and must be percent-encoded; '.' is safe.
            Assert.AreEqual("slot/slot-01/settings/expression.smile",
                CharacterTopics.SlotSettingValue("slot-01", "expression.smile"));
        }

        [Test]
        public void Safe_EncodesNonAsciiAndDelimiters()
        {
            // Space, slash, multibyte characters must be percent-encoded.
            Assert.AreEqual("slot/a%20b/assignment", CharacterTopics.SlotAssignment("a b"));
            Assert.AreEqual("slot/a%2Fb/assignment", CharacterTopics.SlotAssignment("a/b"));
            // Japanese 'あ' = U+3042 = 0xE3 0x81 0x82 in UTF-8.
            Assert.AreEqual("avatars/%E3%81%82/schema", CharacterTopics.AvatarSchema("あ"));
        }

        [Test]
        public void Safe_RejectsNullAndEmpty()
        {
            Assert.Throws<ArgumentNullException>(() => CharacterTopics.Safe(null!));
            Assert.Throws<ArgumentException>(() => CharacterTopics.Safe(string.Empty));
        }

        [Test]
        public void Safe_IsIdempotent()
        {
            const string key = "alpha-99_beta.v1";
            Assert.AreEqual(key, CharacterTopics.Safe(key));
            Assert.AreEqual(CharacterTopics.Safe(key), CharacterTopics.Safe(CharacterTopics.Safe(key)));
        }

        [Test]
        public void SlotCommandAndError_FollowSlotPrefix()
        {
            Assert.AreEqual("slot/p1/command", CharacterTopics.SlotCommand("p1"));
            Assert.AreEqual("slot/p1/error", CharacterTopics.SlotError("p1"));
            Assert.AreEqual("slot/p1/status", CharacterTopics.SlotStatus("p1"));
        }
    }
}
