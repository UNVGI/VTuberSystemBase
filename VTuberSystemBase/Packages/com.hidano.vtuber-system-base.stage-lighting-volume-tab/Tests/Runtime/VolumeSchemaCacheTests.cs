#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the <c>volume/override/schema</c> request + cache semantics implemented by
    /// <see cref="VolumeSchemaCache"/> (Task 3.3, Requirements 6.1, 6.9, 6.11).
    /// </summary>
    [TestFixture]
    public sealed class VolumeSchemaCacheTests
    {
        private static VolumeOverrideSchemaDto SampleSchema()
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
        public async Task FetchAsync_OnSuccess_CachesSchema()
        {
            var ipc = new FakeIpcClient();
            var schema = SampleSchema();
            ipc.RequestResponder = _ => schema;
            var sut = new VolumeSchemaCache(ipc, new FakeDiagnosticsLogger());

            var ok = await sut.FetchAsync();

            Assert.That(ok, Is.True);
            Assert.That(sut.IsLoaded, Is.True);
            Assert.That(sut.Schema.HasValue, Is.True);
            Assert.That(sut.Schema!.Value.Types, Has.Count.EqualTo(1));
            Assert.That(ipc.Requests, Has.Count.EqualTo(1));
            Assert.That(ipc.Requests[0].Topic, Is.EqualTo(StageLightingTopics.VolumeOverrideSchema));
        }

        [Test]
        public async Task FetchAsync_DoesNotRefetchIfAlreadyCached()
        {
            var ipc = new FakeIpcClient();
            ipc.RequestResponder = _ => SampleSchema();
            var sut = new VolumeSchemaCache(ipc, new FakeDiagnosticsLogger());

            await sut.FetchAsync();
            await sut.FetchAsync();

            Assert.That(ipc.Requests, Has.Count.EqualTo(1),
                "second FetchAsync should be served from cache");
        }

        [Test]
        public async Task FetchAsync_OnFailure_ReportsError_AndAllowsRetry()
        {
            var ipc = new FakeIpcClient();
            // Default RequestResponder is null → FakeIpcClient returns Timeout.
            var sut = new VolumeSchemaCache(ipc, new FakeDiagnosticsLogger());

            var first = await sut.FetchAsync();

            Assert.That(first, Is.False);
            Assert.That(sut.IsLoaded, Is.False);
            Assert.That(sut.LastError, Is.Not.Null);
            Assert.That(sut.LastError!.Value.Code, Is.EqualTo(RequestErrorCode.Timeout));

            // Now succeed on retry.
            ipc.RequestResponder = _ => SampleSchema();
            var second = await sut.FetchAsync();
            Assert.That(second, Is.True);
            Assert.That(sut.IsLoaded, Is.True);
            Assert.That(sut.LastError, Is.Null);
        }

        [Test]
        public async Task FetchAsync_AcceptsSchemaContainingUnknownParamKind()
        {
            // Forward-compat: ParamKind.Unknown must round-trip through the cache (Req 6.10
            // skip-and-log happens at the Factory layer, not here).
            var ipc = new FakeIpcClient();
            ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(
                1,
                new List<VolumeOverrideTypeDto>
                {
                    new VolumeOverrideTypeDto(
                        "Custom.Type",
                        "Custom",
                        new List<VolumeOverrideParamDto>
                        {
                            new VolumeOverrideParamDto(
                                "future",
                                ParamKind.Unknown,
                                "Future",
                                new VolumeOverrideParamValueDto(ParamKind.Unknown, null, null, null, null, null, null),
                                null),
                        }),
                });
            var sut = new VolumeSchemaCache(ipc, new FakeDiagnosticsLogger());

            var ok = await sut.FetchAsync();

            Assert.That(ok, Is.True);
            Assert.That(sut.Schema!.Value.Types[0].Params[0].Kind, Is.EqualTo(ParamKind.Unknown));
        }

        [Test]
        public async Task ResetCache_ForcesNextFetchToHitTransport()
        {
            var ipc = new FakeIpcClient();
            ipc.RequestResponder = _ => SampleSchema();
            var sut = new VolumeSchemaCache(ipc, new FakeDiagnosticsLogger());
            await sut.FetchAsync();

            sut.ResetCache();

            await sut.FetchAsync();
            Assert.That(ipc.Requests, Has.Count.EqualTo(2));
        }
    }
}
