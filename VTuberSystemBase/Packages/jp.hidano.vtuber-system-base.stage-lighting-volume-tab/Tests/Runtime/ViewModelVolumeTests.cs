#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks Volume command behaviour (Task 5.4, Requirements 6.1, 6.4-6.9).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelVolumeTests
    {
        private static VolumeOverrideSchemaDto SchemaWithIntensityRange0To10()
        {
            return new VolumeOverrideSchemaDto(
                1,
                new List<VolumeOverrideTypeDto>
                {
                    new VolumeOverrideTypeDto(
                        "UnityEngine.Rendering.Universal.Bloom",
                        "Bloom",
                        new List<VolumeOverrideParamDto>
                        {
                            new VolumeOverrideParamDto(
                                "intensity",
                                ParamKind.Float,
                                "Intensity",
                                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 0f, null, null, null),
                                new VolumeOverrideParamRangeDto(0f, 10f, null, null, null)),
                        }),
                });
        }

        [Test]
        public void SetVolumeOverrideEnabled_PublishesEnabledState()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.ipc.Sent.Clear();

            ctx.vm.SetVolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom", true);

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ctx.ipc.Sent[0].Topic,
                Is.EqualTo("volume/override/UnityEngine.Rendering.Universal.Bloom/enabled"));
            Assert.That(ctx.ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
            Assert.That(ctx.ipc.Sent[0].Payload, Is.EqualTo(true));
        }

        [Test]
        public async Task UpdateVolumeOverrideParam_WithSchemaInRange_PublishesParamState()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.ipc.RequestResponder = _ => SchemaWithIntensityRange0To10();
            ctx.vm.OnActivated();
            await Task.Delay(25);

            ctx.ipc.Sent.Clear();

            var value = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 5f, null, null, null);
            ctx.vm.UpdateVolumeOverrideParam("UnityEngine.Rendering.Universal.Bloom", "intensity", value);

            Assert.That(ctx.ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ctx.ipc.Sent[0].Topic,
                Is.EqualTo("volume/override/UnityEngine.Rendering.Universal.Bloom/intensity"));
            Assert.That(ctx.ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
        }

        [Test]
        public async Task UpdateVolumeOverrideParam_OutOfRange_RaisesValidationError_AndSuppressesSend()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.ipc.RequestResponder = _ => SchemaWithIntensityRange0To10();
            ctx.vm.OnActivated();
            await Task.Delay(25);
            ctx.ipc.Sent.Clear();
            string? errCode = null;
            ctx.vm.OnValidationError += code => errCode = code;

            var value = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 99f, null, null, null);
            ctx.vm.UpdateVolumeOverrideParam("UnityEngine.Rendering.Universal.Bloom", "intensity", value);

            Assert.That(errCode, Is.EqualTo("out_of_range_max"));
            Assert.That(ctx.ipc.Sent, Is.Empty);
        }

        [Test]
        public async Task RetryVolumeSchemaFetch_OnFailure_RaisesWarning()
        {
            var ctx = ViewModelTestFactory.Build();
            // RequestResponder is null -> returns Timeout.
            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;

            var ok = await ctx.vm.RetryVolumeSchemaFetchAsync();

            Assert.That(ok, Is.False);
            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnVolumeSchemaFailed));
        }

        [Test]
        public async Task RetryVolumeSchemaFetch_OnSuccess_PopulatesSchema()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.ipc.RequestResponder = _ => SchemaWithIntensityRange0To10();

            var ok = await ctx.vm.RetryVolumeSchemaFetchAsync();

            Assert.That(ok, Is.True);
            Assert.That(ctx.vm.VolumeSchemaIsLoaded, Is.True);
            Assert.That(ctx.vm.VolumeSchema!.Value.Types[0].Params[0].ParamName, Is.EqualTo("intensity"));
        }
    }
}
