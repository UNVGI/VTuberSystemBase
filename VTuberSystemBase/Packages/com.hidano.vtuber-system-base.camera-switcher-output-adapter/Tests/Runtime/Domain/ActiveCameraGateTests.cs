#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class ActiveCameraGateTests
    {
        private GameObject? _root;
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[ActiveCameraGateTests]");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        [UnityTest]
        public IEnumerator SetActiveTarget_EnablesOnlyMatchingCameraAndVolume()
        {
            yield return null;
            var registry = new CameraEntryRegistry();
            var entries = new[]
            {
                MakeEntry(1, "cam-0001"),
                MakeEntry(2, "cam-0002"),
                MakeEntry(3, "cam-0003"),
            };
            foreach (var e in entries) registry.Upsert(e);

            var gate = new ActiveCameraGate(registry);
            gate.SetActive(new CameraId("cam-0002"));

            Assert.That(entries[0].CameraComponent!.enabled, Is.False);
            Assert.That(entries[1].CameraComponent!.enabled, Is.True);
            Assert.That(entries[2].CameraComponent!.enabled, Is.False);

            Assert.That(entries[0].LocalVolume!.enabled, Is.False);
            Assert.That(entries[1].LocalVolume!.enabled, Is.True);
            Assert.That(entries[2].LocalVolume!.enabled, Is.False);

            Assert.That(gate.Active.HasValue, Is.True);
            Assert.That(gate.Active!.Value.Value, Is.EqualTo("cam-0002"));
        }

        [UnityTest]
        public IEnumerator SetActiveUnknownCameraId_RecordsFailureAndKeepsState()
        {
            yield return null;
            var registry = new CameraEntryRegistry();
            registry.Upsert(MakeEntry(1, "cam-0001"));
            var gate = new ActiveCameraGate(registry);
            gate.SetActive(new CameraId("cam-0001"));

            string? unknownLog = null;
            var gate2 = new ActiveCameraGate(registry, onUnknownCameraId: id => unknownLog = id);
            gate2.SetActive(new CameraId("cam-9999"));

            Assert.That(unknownLog, Is.EqualTo("cam-9999"));
            Assert.That(gate2.Active.HasValue, Is.False);
        }

        [UnityTest]
        public IEnumerator OnCameraRemoved_ClearsActiveWhenItMatches()
        {
            yield return null;
            var registry = new CameraEntryRegistry();
            var first = MakeEntry(1, "cam-0001");
            registry.Upsert(first);
            var gate = new ActiveCameraGate(registry);
            gate.SetActive(new CameraId("cam-0001"));
            gate.OnCameraRemoved(new CameraId("cam-0001"));
            Assert.That(gate.Active.HasValue, Is.False);
        }

        [UnityTest]
        public IEnumerator SetActiveNull_DisablesAllAndClearsActive()
        {
            yield return null;
            var registry = new CameraEntryRegistry();
            var entries = new[] { MakeEntry(1, "cam-0001"), MakeEntry(2, "cam-0002") };
            foreach (var e in entries) registry.Upsert(e);
            var gate = new ActiveCameraGate(registry);
            gate.SetActive(new CameraId("cam-0002"));
            gate.SetActive(null);
            Assert.That(entries[0].CameraComponent!.enabled, Is.False);
            Assert.That(entries[1].CameraComponent!.enabled, Is.False);
            Assert.That(gate.Active.HasValue, Is.False);
        }

        private CameraEntry MakeEntry(int allocOrder, string cameraId)
        {
            var go = new GameObject($"Camera-{cameraId}");
            go.transform.SetParent(_root!.transform, worldPositionStays: false);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;

            var volumeGo = new GameObject($"LocalVolume-{cameraId}");
            volumeGo.transform.SetParent(go.transform, worldPositionStays: false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.enabled = false;

            _spawned.Add(go);
            return new CameraEntry(
                cameraId: new CameraId(cameraId),
                displayName: cameraId,
                type: CameraType.Perspective,
                defaultTransform: new CameraDefaultTransform
                {
                    Position = new[] { 0f, 0f, 0f },
                    Rotation = new[] { 0f, 0f, 0f, 1f },
                    FocalLengthMm = 50f,
                },
                allocOrder: allocOrder,
                gameObject: go,
                cameraComponent: camera,
                localVolume: volume);
        }
    }
}
