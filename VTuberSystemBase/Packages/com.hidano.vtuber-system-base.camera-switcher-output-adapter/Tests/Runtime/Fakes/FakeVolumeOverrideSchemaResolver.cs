#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="IVolumeOverrideSchemaResolver"/>. Returns a
    /// configurable <see cref="VolumeMetadataResponse"/> and records every call.
    /// </summary>
    public sealed class FakeVolumeOverrideSchemaResolver : IVolumeOverrideSchemaResolver
    {
        private readonly Func<VolumeMetadataResponse> _factory;
        public int GetSchemaCallCount { get; private set; }

        public FakeVolumeOverrideSchemaResolver(Func<VolumeMetadataResponse>? factory = null)
        {
            _factory = factory ?? (() => new VolumeMetadataResponse { Overrides = Array.Empty<VolumeOverrideSchema>() });
        }

        public VolumeMetadataResponse GetSchema()
        {
            GetSchemaCallCount++;
            return _factory();
        }

        public static FakeVolumeOverrideSchemaResolver WithEmpty() => new FakeVolumeOverrideSchemaResolver();

        public static FakeVolumeOverrideSchemaResolver Throwing(Exception exception)
            => new FakeVolumeOverrideSchemaResolver(() => throw exception);

        public static FakeVolumeOverrideSchemaResolver WithSchemas(IReadOnlyList<VolumeOverrideSchema> schemas)
            => new FakeVolumeOverrideSchemaResolver(() => new VolumeMetadataResponse { Overrides = schemas });
    }
}
