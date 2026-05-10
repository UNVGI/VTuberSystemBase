#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class GlobalEnabledLocalVolumeBinderTests
    {
        private GameObject? _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("[GlobalEnabledLocalVolumeBinderTests]");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) UnityEngine.Object.Destroy(_parent);
            _parent = null;
        }

        [UnityTest]
        public IEnumerator CreateLocalVolume_AppliesIsGlobalAndDisabled()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var cameraId = new CameraId("cam-0001");
            var volume = binder.CreateLocalVolume(_parent!, cameraId, priority: 5);
            try
            {
                Assert.That(volume.isGlobal, Is.True);
                Assert.That(volume.weight, Is.EqualTo(1f));
                Assert.That(volume.priority, Is.EqualTo(5));
                Assert.That(volume.enabled, Is.False);
                Assert.That(volume.sharedProfile, Is.Not.Null);
                Assert.That(volume.gameObject.name, Is.EqualTo("LocalVolume-cam-0001"));
                Assert.That(volume.transform.parent, Is.SameAs(_parent!.transform));
            }
            finally
            {
                binder.DestroyLocalVolume(volume);
            }
        }

        [UnityTest]
        public IEnumerator AddOverride_AndRemoveOverride_AffectProfileComponentCount()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var volume = binder.CreateLocalVolume(_parent!, new CameraId("cam-0002"), priority: 1);
            try
            {
                Assert.That(volume.sharedProfile!.components.Count, Is.EqualTo(0));

                var add = binder.AddOverride(volume, nameof(Bloom));
                Assert.That(add.Success, Is.True);
                Assert.That(volume.sharedProfile.components.Count, Is.EqualTo(1));

                var addAgain = binder.AddOverride(volume, nameof(Bloom));
                Assert.That(addAgain.Success, Is.True, "AddOverride should be idempotent");
                Assert.That(volume.sharedProfile.components.Count, Is.EqualTo(1));

                var addTone = binder.AddOverride(volume, nameof(Tonemapping));
                Assert.That(addTone.Success, Is.True);
                Assert.That(volume.sharedProfile.components.Count, Is.EqualTo(2));

                var remove = binder.RemoveOverride(volume, nameof(Bloom));
                Assert.That(remove.Success, Is.True);
                Assert.That(volume.sharedProfile.components.Count, Is.EqualTo(1));

                var removeAgain = binder.RemoveOverride(volume, nameof(Bloom));
                Assert.That(removeAgain.Success, Is.True, "RemoveOverride should be idempotent");
            }
            finally
            {
                binder.DestroyLocalVolume(volume);
            }
        }

        [UnityTest]
        public IEnumerator SetOverrideEnabled_TogglesActive()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var volume = binder.CreateLocalVolume(_parent!, new CameraId("cam-0003"), priority: 1);
            try
            {
                binder.AddOverride(volume, nameof(Bloom));
                volume.sharedProfile!.TryGet<Bloom>(out var bloom);
                Assert.That(bloom!.active, Is.True);

                var disable = binder.SetOverrideEnabled(volume, nameof(Bloom), false);
                Assert.That(disable.Success, Is.True);
                Assert.That(bloom.active, Is.False);

                var enable = binder.SetOverrideEnabled(volume, nameof(Bloom), true);
                Assert.That(enable.Success, Is.True);
                Assert.That(bloom.active, Is.True);
            }
            finally
            {
                binder.DestroyLocalVolume(volume);
            }
        }

        [UnityTest]
        public IEnumerator UnknownOverrideType_ReturnsError()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var volume = binder.CreateLocalVolume(_parent!, new CameraId("cam-0004"), priority: 1);
            try
            {
                var add = binder.AddOverride(volume, "TotallyMadeUpOverride");
                Assert.That(add.Success, Is.False);
                Assert.That(add.Reason, Is.EqualTo(VolumeBindFailureReasons.UnknownOverrideType));
            }
            finally
            {
                binder.DestroyLocalVolume(volume);
            }
        }

        [UnityTest]
        public IEnumerator SetVolumeEnabled_TogglesProperty()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var volume = binder.CreateLocalVolume(_parent!, new CameraId("cam-0005"), priority: 1);
            try
            {
                Assert.That(volume.enabled, Is.False);
                binder.SetVolumeEnabled(volume, true);
                Assert.That(volume.enabled, Is.True);
                binder.SetVolumeEnabled(volume, false);
                Assert.That(volume.enabled, Is.False);
            }
            finally
            {
                binder.DestroyLocalVolume(volume);
            }
        }

        [UnityTest]
        public IEnumerator DestroyLocalVolume_RemovesGameObject()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var volume = binder.CreateLocalVolume(_parent!, new CameraId("cam-0006"), priority: 1);
            var go = volume.gameObject;
            binder.DestroyLocalVolume(volume);
            yield return null;
            Assert.That(go == null, Is.True, "Destroyed Volume GameObject should equal null in Unity semantics");
        }
    }
}
