#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;
using VTuberSystemBase.OutputRendererShell.Display;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 3.2: <see cref="BuiltInDisplayRoutingService"/> の振る舞い検証。
    /// 物理ディスプレイ依存を <see cref="IDisplayProbe"/> スタブで除去し、
    /// TargetDisplayIndex = 0 / 1 / 大きすぎる値 の各ケースで期待通りの DisplayAssignmentInfo を返すこと、
    /// および Camera.targetDisplay が設定されることを検証する。
    /// </summary>
    [TestFixture]
    public class BuiltInDisplayRoutingServiceTests
    {
        private GameObject? _cameraGo;
        private Camera _camera = null!;
        private OutputShellLogger _logger = null!;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("RoutingTest_Camera");
            _camera = _cameraGo.AddComponent<Camera>();
            _logger = new OutputShellLogger(LogLevel.Verbose);
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

        [UnityTest]
        [Description("TargetDisplayIndex = 0 で Display 0 がアクティブ化され、フォールバックは発生しない")]
        public IEnumerator Activate_TargetDisplayZero_NoFallback()
        {
            var probe = new StubDisplayProbe { DisplayCount = 2, IsEditor = false };
            var sut = new BuiltInDisplayRoutingService(_logger, probe);

            var info = sut.Activate(_camera, new DisplayRoutingConfig
            {
                TargetDisplayIndex = 0,
                SuppressEditorWarning = true,
            });
            yield return null;

            Assert.AreEqual(0, info.RequestedDisplayIndex);
            Assert.AreEqual(0, info.EffectiveDisplayIndex);
            Assert.IsFalse(info.IsFallbackActive);
            Assert.AreEqual(0, _camera.targetDisplay);
        }

        [UnityTest]
        [Description("TargetDisplayIndex = 1 で Display 1 がアクティブ化される（標準ケース、メイン出力 = Display 2）")]
        public IEnumerator Activate_TargetDisplayOne_ActivatesDisplayOne()
        {
            var probe = new StubDisplayProbe { DisplayCount = 2, IsEditor = false };
            var sut = new BuiltInDisplayRoutingService(_logger, probe);

            var info = sut.Activate(_camera, new DisplayRoutingConfig
            {
                TargetDisplayIndex = 1,
                SuppressEditorWarning = true,
            });
            yield return null;

            Assert.AreEqual(1, info.RequestedDisplayIndex);
            Assert.AreEqual(1, info.EffectiveDisplayIndex);
            Assert.IsFalse(info.IsFallbackActive);
            Assert.AreEqual(1, _camera.targetDisplay);
            Assert.Contains(1, probe.ActivatedIndices);
        }

        [UnityTest]
        [Description("TargetDisplayIndex が DisplayCount を超える場合は Display 0 へフォールバックし IsFallbackActive=true / 警告ログ")]
        public IEnumerator Activate_TargetDisplayOutOfRange_FallsBackToZero_WithWarning()
        {
            var probe = new StubDisplayProbe { DisplayCount = 1, IsEditor = false };
            var sut = new BuiltInDisplayRoutingService(_logger, probe);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Requested Display index 5"));
            var info = sut.Activate(_camera, new DisplayRoutingConfig
            {
                TargetDisplayIndex = 5,
                SuppressEditorWarning = true,
            });
            yield return null;

            Assert.AreEqual(5, info.RequestedDisplayIndex);
            Assert.AreEqual(0, info.EffectiveDisplayIndex);
            Assert.IsTrue(info.IsFallbackActive);
            Assert.IsNotNull(info.DiagnosticMessage);
            Assert.AreEqual(0, _camera.targetDisplay);
        }

        [UnityTest]
        [Description("Editor PlayMode 上では IsEditorLimitedMode=true となり、SuppressEditorWarning=false で Info ログが出る")]
        public IEnumerator Activate_InEditor_RecordsEditorLimitedMode_AndLogsInfo()
        {
            var probe = new StubDisplayProbe { DisplayCount = 2, IsEditor = true };
            var sut = new BuiltInDisplayRoutingService(_logger, probe);

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("Display.Activate is no-op in Editor PlayMode"));
            var info = sut.Activate(_camera, new DisplayRoutingConfig
            {
                TargetDisplayIndex = 1,
                SuppressEditorWarning = false,
            });
            yield return null;

            Assert.IsTrue(info.IsEditorLimitedMode);
        }

        [UnityTest]
        [Description("GetAssignment は直近の Activate 結果を返し、Activate 前は default を返す")]
        public IEnumerator GetAssignment_ReturnsLatestActivateResult()
        {
            var probe = new StubDisplayProbe { DisplayCount = 2, IsEditor = false };
            var sut = new BuiltInDisplayRoutingService(_logger, probe);

            Assert.AreEqual(default(DisplayAssignmentInfo), sut.GetAssignment(),
                "Activate 前は default(DisplayAssignmentInfo) を返す");

            var info = sut.Activate(_camera, new DisplayRoutingConfig
            {
                TargetDisplayIndex = 0,
                SuppressEditorWarning = true,
            });
            yield return null;

            Assert.AreEqual(info, sut.GetAssignment(),
                "Activate 後は同じ DisplayAssignmentInfo を返す");
        }

        private sealed class StubDisplayProbe : IDisplayProbe
        {
            public int DisplayCount { get; set; } = 1;
            public bool IsEditor { get; set; }
            public List<int> ActivatedIndices { get; } = new();
            public List<FullScreenMode> AppliedFullScreenModes { get; } = new();

            public void ActivateDisplay(int index) => ActivatedIndices.Add(index);
            public void SetFullScreenMode(FullScreenMode mode) => AppliedFullScreenModes.Add(mode);
        }
    }
}
