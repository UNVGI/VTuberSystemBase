#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks Light command behaviour (Task 5.3, Requirements 4.3, 4.4, 4.5, 4.6, 4.7,
    /// 4.10, 5.1, 5.2, 5.3, 5.4, 5.7, 5.8, 5.11).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelLightTests
    {
        private static LightInitialDto SampleInitial()
        {
            return new LightInitialDto(
                LightTypeDto.Directional,
                new Vector3Dto(0f, 0f, 0f),
                new ColorDto(1f, 1f, 1f, 1f),
                1.0f, 0f, 30f, "Sun");
        }

        [Test]
        public void AddLight_PublishesAddEvent_AndReturnsCorrelationId()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            var cid = ctx.vm.AddLight(SampleInitial());

            Assert.That(cid, Is.Not.Null.And.Not.Empty);
            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ctx.ipc.Sent[0].Topic, Is.EqualTo(StageLightingTopics.LightCommand));
            var dto = (LightCommandDto)ctx.ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("add"));
            Assert.That(dto.Initial.HasValue, Is.True);
            Assert.That(dto.Initial!.Value.DisplayName, Is.EqualTo("Sun"));
        }

        [Test]
        public async Task AddLight_TimeoutAfterFiveSeconds_RaisesWarning()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            ctx.vm.AddLight(SampleInitial());

            ctx.clock.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(20); // give the watcher a chance.

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnLightAddTimeout));
        }

        [Test]
        public async Task LightAddedEvent_BeforeTimeout_DoesNotRaiseWarning()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            ctx.vm.AddLight(SampleInitial());

            ctx.ipc.Emit(StageLightingTopics.LightAdded,
                new LightAddedDto("light-1", SampleInitial()),
                MessageKind.Event);

            ctx.clock.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(20);

            Assert.That(warn, Is.Null);
        }

        [Test]
        public void RemoveLight_PublishesRemoveEvent()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            ctx.vm.RemoveLight("light-1");

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            var dto = (LightCommandDto)ctx.ipc.Sent[0].Payload!;
            Assert.That(dto.Op, Is.EqualTo("remove"));
            Assert.That(dto.LightId, Is.EqualTo("light-1"));
        }

        [Test]
        public void UpdateLightProperty_Intensity_ValidatedAndSent()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            ctx.vm.UpdateLightProperty("light-1", StageLightingTopics.PropertyIntensity, 2.5f);

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ctx.ipc.Sent[0].Topic, Is.EqualTo("light/light-1/intensity"));
            Assert.That(ctx.ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
            Assert.That(ctx.ipc.Sent[0].Payload, Is.EqualTo(2.5f));
        }

        [Test]
        public void UpdateLightProperty_OutOfRange_RaisesValidationError_AndSuppressesSend()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();
            string? errCode = null;
            ctx.vm.OnValidationError += code => errCode = code;

            ctx.vm.UpdateLightProperty("light-1", StageLightingTopics.PropertyIntensity, -1f);

            Assert.That(errCode, Is.EqualTo("out_of_range_min"));
            Assert.That(ctx.ipc.Sent, Is.Empty);
        }

        [Test]
        public void SetLightPropertyDragging_TogglesPerProperty()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            ctx.vm.SetLightPropertyDragging("light-1", "intensity", true);
            Assert.That(ctx.vm.IsLightPropertyDragging("light-1", "intensity"), Is.True);
            Assert.That(ctx.vm.IsLightPropertyDragging("light-1", "color"), Is.False);

            ctx.vm.SetLightPropertyDragging("light-1", "intensity", false);
            Assert.That(ctx.vm.IsLightPropertyDragging("light-1", "intensity"), Is.False);
        }

        [Test]
        public void LightError_RaisesOperationWarning()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            ctx.ipc.Emit(StageLightingTopics.LightError,
                new LightErrorDto(null, "cid", "limit_exceeded", "boom"),
                MessageKind.Event);

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnLightAddFailed));
        }
    }
}
