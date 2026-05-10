#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.PlayMode
{
    public sealed class StagePreviewHostPlayModeTests
    {
        [UnityTest]
        public IEnumerator Awake_AllocatesRT_RegistersLocator_AndDestroyReleases()
        {
            var go = new GameObject("PreviewCamera");
            try
            {
                go.AddComponent<Camera>();
                var host = go.AddComponent<StagePreviewHost>();
                yield return null; // wait for Awake.

                Assert.That(host.IsReady, Is.True);
                Assert.That(host.CurrentRenderTexture, Is.Not.Null);
                Assert.That(host.CurrentRenderTexture!.IsCreated(), Is.True);
                Assert.That(StagePreviewHostLocator.Current, Is.SameAs(host));

                Object.Destroy(go);
                yield return null;

                Assert.That(StagePreviewHostLocator.Current, Is.Null);
            }
            finally
            {
                if (go != null) Object.Destroy(go);
            }
        }

        [UnityTest]
        public IEnumerator SetEnabled_TogglesCamera()
        {
            var go = new GameObject("PreviewCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                var host = go.AddComponent<StagePreviewHost>();
                yield return null;

                host.SetEnabled(false);
                Assert.That(cam.enabled, Is.False);
                host.SetEnabled(true);
                Assert.That(cam.enabled, Is.True);
            }
            finally { if (go != null) Object.Destroy(go); }
        }
    }
}
