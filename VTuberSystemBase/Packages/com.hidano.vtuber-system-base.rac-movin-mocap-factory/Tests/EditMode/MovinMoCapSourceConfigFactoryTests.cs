using System.Collections.Generic;
using NUnit.Framework;
using RealtimeAvatarController.Core;
using RealtimeAvatarController.MoCap.Movin;
using UnityEngine;

namespace VTuberSystemBase.RacMovinMoCapFactory.Tests.EditMode
{
    [TestFixture]
    public sealed class MovinMoCapSourceConfigFactoryTests
    {
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var createdObject in _createdObjects)
            {
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void Build_ReturnsDescriptorWithMovinSourceTypeId()
        {
            var factory = new MovinMoCapSourceConfigFactory();

            var descriptor = BuildTracked(factory, "slot-A");

            Assert.That(descriptor.SourceTypeId, Is.EqualTo(MovinMoCapSourceFactory.MovinSourceTypeId));
        }

        [Test]
        public void Build_ReturnsDescriptorWithMovinConfig()
        {
            var factory = new MovinMoCapSourceConfigFactory();

            var descriptor = BuildTracked(factory, "slot-A");

            Assert.That(descriptor.Config, Is.InstanceOf<MovinMoCapSourceConfig>());
        }

        [Test]
        public void Build_AppliesPortRootBoneNameBoneClass()
        {
            var factory = new MovinMoCapSourceConfigFactory(
                port: 12345,
                rootBoneName: "Hips",
                boneClass: "Humanoid");

            var descriptor = BuildTracked(factory, "slot-A");
            var config = descriptor.Config as MovinMoCapSourceConfig;

            Assert.That(config, Is.Not.Null);
            Assert.That(config.port, Is.EqualTo(12345));
            Assert.That(config.rootBoneName, Is.EqualTo("Hips"));
            Assert.That(config.boneClass, Is.EqualTo("Humanoid"));
        }

        [Test]
        public void Build_ProducesDistinctConfigInstances()
        {
            var factory = new MovinMoCapSourceConfigFactory();

            var first = BuildTracked(factory, "slot-A");
            var second = BuildTracked(factory, "slot-A");

            Assert.That(first.Config, Is.InstanceOf<MovinMoCapSourceConfig>());
            Assert.That(second.Config, Is.InstanceOf<MovinMoCapSourceConfig>());
            Assert.That(object.ReferenceEquals(first.Config, second.Config), Is.False);
        }

        [Test]
        public void Build_NameContainsSlotId()
        {
            var factory = new MovinMoCapSourceConfigFactory();

            var descriptor = BuildTracked(factory, "slot-X");

            Assert.That(descriptor.Config.name, Does.Contain("slot-X"));
        }

        [Test]
        public void Constructor_NormalizesNullStringsToEmpty()
        {
            var factory = new MovinMoCapSourceConfigFactory(rootBoneName: null, boneClass: null);

            Assert.That(factory.RootBoneName, Is.EqualTo(string.Empty));
            Assert.That(factory.BoneClass, Is.EqualTo(string.Empty));
        }

        private MoCapSourceDescriptor BuildTracked(MovinMoCapSourceConfigFactory factory, string slotId)
        {
            var descriptor = factory.Build(slotId);
            if (descriptor.Config != null)
            {
                _createdObjects.Add(descriptor.Config);
            }

            return descriptor;
        }
    }
}
