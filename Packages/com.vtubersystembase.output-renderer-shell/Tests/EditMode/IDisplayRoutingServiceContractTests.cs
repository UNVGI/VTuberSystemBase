#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.EditModeTests.Fakes;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests
{
    /// <summary>
    /// Task 3.1: <see cref="IDisplayRoutingService"/> の契約検証。
    /// Fake 実装が「Activate 後に GetAssignment が同値を返す」「IsFallbackActive が反映される」契約を満たすことを確認。
    /// </summary>
    [TestFixture]
    public class IDisplayRoutingServiceContractTests
    {
        private GameObject? _cameraGo;
        private Camera _camera = null!;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("ContractTest_Camera");
            _camera = _cameraGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraGo != null)
            {
                Object.DestroyImmediate(_cameraGo);
                _cameraGo = null;
            }
        }

        [Test]
        [Description("Activate 後に GetAssignment が同じ DisplayAssignmentInfo を返す")]
        public void Activate_ThenGetAssignment_ReturnsSameInfo()
        {
            var fake = new FakeDisplayRoutingService();
            var config = new DisplayRoutingConfig { TargetDisplayIndex = 1 };

            var result = fake.Activate(_camera, config);
            var roundTrip = fake.GetAssignment();

            Assert.AreEqual(result, roundTrip,
                "Activate の戻り値と GetAssignment の戻り値が等価であること");
            Assert.AreEqual(1, result.RequestedDisplayIndex);
            Assert.AreEqual(1, result.EffectiveDisplayIndex);
            Assert.IsFalse(result.IsFallbackActive);
        }

        [Test]
        [Description("StagedResult で IsFallbackActive=true を返した場合、IsFallbackActive プロパティが true になる")]
        public void StagedResult_WithFallback_IsFallbackActiveBecomesTrue()
        {
            var fake = new FakeDisplayRoutingService
            {
                StagedResult = new DisplayAssignmentInfo
                {
                    RequestedDisplayIndex = 5,
                    EffectiveDisplayIndex = 0,
                    IsFallbackActive = true,
                    IsEditorLimitedMode = false,
                    DiagnosticMessage = "fallback to Display 0",
                },
            };

            Assert.IsFalse(fake.IsFallbackActive,
                "Activate 前は IsFallbackActive = false");

            var result = fake.Activate(_camera, new DisplayRoutingConfig { TargetDisplayIndex = 5 });

            Assert.IsTrue(fake.IsFallbackActive,
                "Activate 後に IsFallbackActive が true になる");
            Assert.IsTrue(result.IsFallbackActive);
            Assert.AreEqual(0, result.EffectiveDisplayIndex);
            Assert.AreEqual("fallback to Display 0", result.DiagnosticMessage);
        }

        [Test]
        [Description("Activate 呼び出しは履歴に記録される")]
        public void Activate_RecordsCallHistory()
        {
            var fake = new FakeDisplayRoutingService();
            var c1 = new DisplayRoutingConfig { TargetDisplayIndex = 0 };
            var c2 = new DisplayRoutingConfig { TargetDisplayIndex = 1 };

            fake.Activate(_camera, c1);
            fake.Activate(_camera, c2);

            Assert.AreEqual(2, fake.Calls.Count);
            Assert.AreSame(_camera, fake.Calls[0].Camera);
            Assert.AreEqual(0, fake.Calls[0].Config.TargetDisplayIndex);
            Assert.AreEqual(1, fake.Calls[1].Config.TargetDisplayIndex);
        }

        [Test]
        [Description("Dispose 後に IsDisposed が true になる（IDisposable 契約）")]
        public void Dispose_SetsIsDisposed()
        {
            var fake = new FakeDisplayRoutingService();
            Assert.IsFalse(fake.IsDisposed);
            fake.Dispose();
            Assert.IsTrue(fake.IsDisposed);
        }

        [Test]
        [Description("DisplayRoutingConfig の既定値は Display 2 (index 1) / FullScreenWindow / SuppressEditorWarning=false")]
        public void DisplayRoutingConfig_Defaults_AreSafeForFreshInstance()
        {
            var config = new DisplayRoutingConfig();
            Assert.AreEqual(1, config.TargetDisplayIndex);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, config.FullScreenMode);
            Assert.IsFalse(config.SuppressEditorWarning);
        }
    }
}
