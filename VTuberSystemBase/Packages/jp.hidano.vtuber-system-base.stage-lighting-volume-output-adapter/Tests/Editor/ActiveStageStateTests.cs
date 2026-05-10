#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class ActiveStageStateTests
    {
        [Test]
        public void Defaults_AreEmpty()
        {
            var s = new ActiveStageState();
            Assert.That(s.CurrentStage, Is.Null);
            Assert.That(s.CurrentAddressableKey, Is.Null);
            Assert.That(s.IsLoading, Is.False);
        }

        [Test]
        public void SetLoading_Toggles()
        {
            var s = new ActiveStageState();
            s.SetLoading(true);
            Assert.That(s.IsLoading, Is.True);
            s.SetLoading(false);
            Assert.That(s.IsLoading, Is.False);
        }

        [Test]
        public void SetActive_PopulatesAndClearsLoadingFlag()
        {
            var s = new ActiveStageState();
            s.SetLoading(true);
            var go = new GameObject("Stage_Test");
            try
            {
                s.SetActive(go, "Stages/Test");
                Assert.That(s.CurrentStage, Is.SameAs(go));
                Assert.That(s.CurrentAddressableKey, Is.EqualTo("Stages/Test"));
                Assert.That(s.IsLoading, Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Clear_ResetsAllFields()
        {
            var s = new ActiveStageState();
            var go = new GameObject("Stage_Test");
            try
            {
                s.SetActive(go, "k");
                s.Clear();
                Assert.That(s.CurrentStage, Is.Null);
                Assert.That(s.CurrentAddressableKey, Is.Null);
                Assert.That(s.IsLoading, Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
