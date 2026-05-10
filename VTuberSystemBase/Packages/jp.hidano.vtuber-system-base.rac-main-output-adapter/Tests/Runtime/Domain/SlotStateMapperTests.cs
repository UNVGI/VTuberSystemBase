using NUnit.Framework;
using RealtimeAvatarController.Core;
using VTuberSystemBase.RacMainOutputAdapter.Domain;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class SlotStateMapperTests
    {
        [Test]
        public void Active_NotAssigning_MapsToAssigned()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Active, false), Is.EqualTo("Assigned"));
        }

        [Test]
        public void Active_Assigning_MapsToAssigning()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Active, true), Is.EqualTo("Assigning"));
        }

        [Test]
        public void Created_NotAssigning_MapsToEmpty()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Created, false), Is.EqualTo("Empty"));
        }

        [Test]
        public void Disposed_MapsToEmpty()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Disposed, false), Is.EqualTo("Empty"));
        }

        [Test]
        public void Inactive_MapsToEmpty()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Inactive, false), Is.EqualTo("Empty"));
        }

        [Test]
        public void Assigning_OverridesAnyState()
        {
            Assert.That(SlotStateMapper.Map(SlotState.Disposed, true), Is.EqualTo("Assigning"));
        }
    }
}
