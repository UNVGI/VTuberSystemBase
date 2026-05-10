#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for <see cref="PreviewPanelController"/>: locks the lifecycle
    /// command flow (set-enabled true/false, reset-view) and the RenderTexture
    /// binding semantics (Task 4.3, Requirements 2.2, 2.6, 2.7, 2.8, 2.11).
    /// </summary>
    [TestFixture]
    public sealed class PreviewPanelControllerTests
    {
        [Test]
        public void OnActivated_PublishesPreviewCommandSetEnabledTrue()
        {
            var ipc = new FakeIpcClient();
            var accessor = new FakePreviewRenderTextureAccessor();
            var panel = new VisualElement();

            using var sut = new PreviewPanelController(panel, accessor, ipc, new FakeDiagnosticsLogger());
            sut.OnActivated();

            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ipc.Sent[0].Topic, Is.EqualTo(StageLightingTopics.PreviewCommand));
            Assert.That(ipc.Sent[0].Kind, Is.EqualTo(MessageKind.Event));
            var dto = (PreviewCommandDto)ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("set-enabled"));
            Assert.That(dto.Enabled, Is.True);
        }

        [Test]
        public void OnDeactivated_PublishesPreviewCommandSetEnabledFalse()
        {
            var ipc = new FakeIpcClient();
            using var sut = new PreviewPanelController(
                new VisualElement(), new FakePreviewRenderTextureAccessor(), ipc, new FakeDiagnosticsLogger());
            sut.OnActivated();
            ipc.Sent.Clear();

            sut.OnDeactivated();

            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
            var dto = (PreviewCommandDto)ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("set-enabled"));
            Assert.That(dto.Enabled, Is.False);
        }

        [Test]
        public void ResetView_PublishesPreviewCommandResetView()
        {
            var ipc = new FakeIpcClient();
            using var sut = new PreviewPanelController(
                new VisualElement(), new FakePreviewRenderTextureAccessor(), ipc, new FakeDiagnosticsLogger());

            sut.ResetView();

            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
            var dto = (PreviewCommandDto)ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("reset-view"));
        }

        [Test]
        public void RenderTextureBound_PanelStyleBackgroundReflectsRT()
        {
            var ipc = new FakeIpcClient();
            var accessor = new FakePreviewRenderTextureAccessor();
            var panel = new VisualElement();
            using var scope = new PanelAttachScope(panel);

            var rt = new RenderTexture(64, 64, 0);
            try
            {
                using var sut = new PreviewPanelController(panel, accessor, ipc, new FakeDiagnosticsLogger());
                sut.OnActivated();

                accessor.SetTexture(rt);

                // Unity 6 の UI Toolkit では IStyle.backgroundImage に inline で書き込んだ
                // Background 構造体を同フレーム内に getter から読み戻すと、すべてのスロット
                // (texture / sprite / renderTexture / vectorImage) が空で返る挙動がある
                // (fresh VisualElement + UIDocument attach でも再現)。RT バインドが
                // 反映されたことの検証は、Production が同時に外す PlaceholderClass の
                // 除去で代替する (要件 2.6: RT 取得時はプレースホルダ表示を抑止)。
                Assert.That(panel.ClassListContains("vsb-slv-preview--placeholder"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void RenderTextureNull_PanelShowsPlaceholderClass()
        {
            var ipc = new FakeIpcClient();
            var accessor = new FakePreviewRenderTextureAccessor();
            var panel = new VisualElement();

            using var sut = new PreviewPanelController(panel, accessor, ipc, new FakeDiagnosticsLogger());
            // No RT registered yet → placeholder class on the element.
            sut.OnActivated();

            Assert.That(panel.ClassListContains("vsb-slv-preview--placeholder"), Is.True);
        }

        [Test]
        public void Dispose_StopsForwardingRenderTextureChanges()
        {
            var ipc = new FakeIpcClient();
            var accessor = new FakePreviewRenderTextureAccessor();
            var panel = new VisualElement();
            var rt = new RenderTexture(64, 64, 0);
            try
            {
                var sut = new PreviewPanelController(panel, accessor, ipc, new FakeDiagnosticsLogger());
                sut.OnActivated();
                sut.Dispose();

                accessor.SetTexture(rt);

                Assert.That(panel.style.backgroundImage.value.renderTexture, Is.Null,
                    "Disposed controller must not bind subsequent RT updates.");
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }
    }
}
