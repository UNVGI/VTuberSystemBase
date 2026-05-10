using System;
using System.Collections.Generic;
using NUnit.Framework;
using RealtimeAvatarController.Core;
using VTuberSystemBase.RacMainOutputAdapter.Domain;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class SlotErrorCodeMapperTests
    {
        [Test]
        public void ApplyFailure_AlwaysApplyFailed()
        {
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.ApplyFailure, null), Is.EqualTo("ApplyFailed"));
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.ApplyFailure, new Exception("anything")), Is.EqualTo("ApplyFailed"));
        }

        [Test]
        public void InitFailure_KeyNotFoundException_MapsKeyNotFound()
        {
            // KeyNotFoundException は型名に "KeyNotFound" を含む
            var ex = new KeyNotFoundException("avatar 'miku' is missing");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("KeyNotFound"));
        }

        [Test]
        public void InitFailure_AddressableInTypeName_MapsKeyNotFound()
        {
            // 型名には "Addressable" を含むが、ここではメッセージで代用
            var ex = new InvalidOperationException("Addressable provider failed");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("KeyNotFound"));
        }

        [Test]
        public void InitFailure_AvatarKeyMessage_MapsKeyNotFound()
        {
            var ex = new InvalidOperationException("AvatarKey not registered");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("KeyNotFound"));
        }

        [Test]
        public void InitFailure_MoCapMessage_MapsMotionPipelineInit()
        {
            var ex = new InvalidOperationException("MoCap source initialization failed");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("MotionPipelineInit"));
        }

        [Test]
        public void InitFailure_SourceMessage_MapsMotionPipelineInit()
        {
            var ex = new InvalidOperationException("Source factory missing");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("MotionPipelineInit"));
        }

        [Test]
        public void InitFailure_OtherException_MapsUnknown()
        {
            var ex = new InvalidOperationException("totally unrelated message");
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex), Is.EqualTo("Unknown"));
        }

        [Test]
        public void InitFailure_NullException_MapsUnknown()
        {
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, null), Is.EqualTo("Unknown"));
        }

        [Test]
        public void RegistryConflict_AlwaysUnknown()
        {
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.RegistryConflict, null), Is.EqualTo("Unknown"));
        }

        [Test]
        public void VmcReceive_AlwaysUnknown()
        {
            Assert.That(SlotErrorCodeMapper.Map(SlotErrorCategory.VmcReceive, null), Is.EqualTo("Unknown"));
        }
    }
}
