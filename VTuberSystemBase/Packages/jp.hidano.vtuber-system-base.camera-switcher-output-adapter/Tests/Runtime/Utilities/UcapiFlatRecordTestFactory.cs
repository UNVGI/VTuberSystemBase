#nullable enable
using UCAPI4Unity.Runtime.UnityCamera;
using UnityEngine;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities
{
    /// <summary>
    /// Test helper that produces real UCAPI Flat Record blobs by serialising a
    /// transient <see cref="Camera"/> with <see cref="UcApi4UnityCamera.SerializeFromCamera"/>.
    /// PlayMode-only — uses real Unity Engine APIs.
    /// </summary>
    public static class UcapiFlatRecordTestFactory
    {
        /// <summary>
        /// Builds a Flat Record blob whose decoded transform / lens values match the
        /// supplied <paramref name="position"/> / <paramref name="eulerRotation"/> /
        /// <paramref name="focalLengthMm"/>.
        /// </summary>
        public static byte[] CreateBlob(Vector3 position, Vector3 eulerRotation, float focalLengthMm = 50f)
        {
            var go = new GameObject("[UcapiFlatRecordTestFactory]");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.usePhysicalProperties = true;
                cam.focalLength = focalLengthMm;
                cam.sensorSize = new Vector2(36f, 24f);
                cam.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerRotation));
                return UcApi4UnityCamera.SerializeFromCamera(cam);
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        /// <summary>Builds a default Flat Record blob (origin / identity / 50 mm).</summary>
        public static byte[] CreateDefaultBlob() => CreateBlob(Vector3.zero, Vector3.zero);
    }
}
