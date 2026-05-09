#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class OscMessageRouterTests
    {
        [Test]
        public void Route_KnownCameraId_InvokesApply()
        {
            var entry = MakeEntry("cam-0001");
            var unknownLog = new List<string>();
            var applied = new List<(string cameraId, byte[] blob)>();
            var router = new OscMessageRouter(
                tryResolve: id => id.Value == "cam-0001" ? entry : null,
                onUnknownCameraId: id => unknownLog.Add(id));

            var blob = new byte[] { 1, 2, 3 };
            var msg = new OscReceivedMessage("cam-0001", blob);
            router.Route(in msg, (e, b) => applied.Add((e.CameraId.Value, b)));

            Assert.That(applied.Count, Is.EqualTo(1));
            Assert.That(applied[0].cameraId, Is.EqualTo("cam-0001"));
            Assert.That(applied[0].blob, Is.SameAs(blob));
            Assert.That(unknownLog, Is.Empty);
        }

        [Test]
        public void Route_UnknownCameraId_InvokesFailureCallbackOnly()
        {
            var unknownLog = new List<string>();
            var applied = new List<string>();
            var router = new OscMessageRouter(
                tryResolve: _ => null,
                onUnknownCameraId: id => unknownLog.Add(id));

            var msg = new OscReceivedMessage("cam-9999", new byte[] { 0 });
            router.Route(in msg, (e, _) => applied.Add(e.CameraId.Value));

            Assert.That(applied, Is.Empty);
            Assert.That(unknownLog, Is.EqualTo(new[] { "cam-9999" }));
        }

        private static CameraEntry MakeEntry(string cameraId) => new CameraEntry(
            cameraId: new CameraId(cameraId),
            displayName: "Cam",
            type: CameraType.Perspective,
            defaultTransform: new CameraDefaultTransform
            {
                Position = new[] { 0f, 0f, 0f },
                Rotation = new[] { 0f, 0f, 0f, 1f },
                FocalLengthMm = 50f,
            },
            allocOrder: 1,
            gameObject: null,
            cameraComponent: null,
            localVolume: null);
    }
}
