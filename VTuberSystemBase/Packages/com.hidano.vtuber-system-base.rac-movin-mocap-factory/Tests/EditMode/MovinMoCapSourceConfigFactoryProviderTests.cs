using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RealtimeAvatarController.MoCap.Movin;
using UnityEngine;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMovinMoCapFactory.Tests.EditMode
{
    [TestFixture]
    public sealed class MovinMoCapSourceConfigFactoryProviderTests
    {
        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();
        private GameObject _gameObject;
        private MovinMoCapSourceConfigFactoryProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("MovinProviderTests");
            _provider = _gameObject.AddComponent<MovinMoCapSourceConfigFactoryProvider>();
        }

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

            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void DefaultPortIsExpectedValue()
        {
            var port = GetSerializedField<int>("port");

            Assert.That(port, Is.EqualTo(11235));
        }

        [Test]
        public void Factory_PropagatesSerializedValues()
        {
            SetSerializedField("port", 12345);
            SetSerializedField("rootBoneName", "Hips");
            SetSerializedField("boneClass", "Humanoid");

            var descriptor = _provider.Factory.Build("slot-A");
            if (descriptor.Config != null)
            {
                _createdObjects.Add(descriptor.Config);
            }

            var config = descriptor.Config as MovinMoCapSourceConfig;

            Assert.That(config, Is.Not.Null);
            Assert.That(config.port, Is.EqualTo(12345));
            Assert.That(config.rootBoneName, Is.EqualTo("Hips"));
            Assert.That(config.boneClass, Is.EqualTo("Humanoid"));
        }

        [Test]
        public void Factory_ReturnsNonNullInstance()
        {
            Assert.That(_provider.Factory, Is.Not.Null);
        }

        [Test]
        public void Factory_ImplementsIMoCapSourceConfigFactoryProvider()
        {
            Assert.That(_provider, Is.InstanceOf<IMoCapSourceConfigFactoryProvider>());
        }

        private T GetSerializedField<T>(string fieldName)
        {
            return (T)GetSerializedField(fieldName).GetValue(_provider);
        }

        private void SetSerializedField(string fieldName, object value)
        {
            GetSerializedField(fieldName).SetValue(_provider, value);
        }

        private static FieldInfo GetSerializedField(string fieldName)
        {
            var field = typeof(MovinMoCapSourceConfigFactoryProvider).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Expected serialized field '{fieldName}' to exist.");
            return field;
        }
    }
}
