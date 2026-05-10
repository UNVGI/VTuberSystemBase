#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class LightRegistryTests
    {
        private static LightEntry MakeEntry(string id, string name, LightTypeDto type)
        {
            var go = new GameObject($"Light_{id}");
            var light = go.AddComponent<Light>();
            light.type = type switch
            {
                LightTypeDto.Directional => LightType.Directional,
                LightTypeDto.Point => LightType.Point,
                LightTypeDto.Spot => LightType.Spot,
                _ => LightType.Rectangle,
            };
            var initial = new LightInitialDto(type, default, default, 1f, 10f, 30f, name);
            return new LightEntry(id, go, light, initial);
        }

        [Test]
        public void Add_TryGet_Roundtrip()
        {
            var r = new LightRegistry();
            var e = MakeEntry("a", "L1", LightTypeDto.Point);
            try
            {
                r.Add("a", e);
                Assert.That(r.TryGet("a", out var got), Is.True);
                Assert.That(got, Is.SameAs(e));
            }
            finally { Object.DestroyImmediate(e.GameObject); }
        }

        [Test]
        public void Remove_RemovesFromOrderAndDictionary()
        {
            var r = new LightRegistry();
            var e1 = MakeEntry("a", "L1", LightTypeDto.Point);
            var e2 = MakeEntry("b", "L2", LightTypeDto.Spot);
            try
            {
                r.Add("a", e1); r.Add("b", e2);
                Assert.That(r.Remove("a"), Is.True);
                Assert.That(r.TryGet("a", out _), Is.False);
                Assert.That(r.AllLightIds.Count, Is.EqualTo(1));
                Assert.That(r.AllLightIds[0], Is.EqualTo("b"));
                Assert.That(r.Remove("missing"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(e1.GameObject);
                Object.DestroyImmediate(e2.GameObject);
            }
        }

        [Test]
        public void ToListDto_PreservesInsertionOrder()
        {
            var r = new LightRegistry();
            var e1 = MakeEntry("a", "First", LightTypeDto.Directional);
            var e2 = MakeEntry("b", "Second", LightTypeDto.Point);
            var e3 = MakeEntry("c", "Third", LightTypeDto.Spot);
            try
            {
                r.Add("a", e1); r.Add("b", e2); r.Add("c", e3);
                var dto = r.ToListDto();
                Assert.That(dto.Items.Count, Is.EqualTo(3));
                Assert.That(dto.Items[0].LightId, Is.EqualTo("a"));
                Assert.That(dto.Items[0].DisplayName, Is.EqualTo("First"));
                Assert.That(dto.Items[0].Type, Is.EqualTo(LightTypeDto.Directional));
                Assert.That(dto.Items[1].LightId, Is.EqualTo("b"));
                Assert.That(dto.Items[2].LightId, Is.EqualTo("c"));
            }
            finally
            {
                Object.DestroyImmediate(e1.GameObject);
                Object.DestroyImmediate(e2.GameObject);
                Object.DestroyImmediate(e3.GameObject);
            }
        }

        [Test]
        public void Clear_EmptiesAll()
        {
            var r = new LightRegistry();
            var e1 = MakeEntry("a", "First", LightTypeDto.Directional);
            try
            {
                r.Add("a", e1);
                r.Clear();
                Assert.That(r.Count, Is.EqualTo(0));
                Assert.That(r.AllLightIds.Count, Is.EqualTo(0));
                Assert.That(r.TryGet("a", out _), Is.False);
            }
            finally { Object.DestroyImmediate(e1.GameObject); }
        }
    }
}
