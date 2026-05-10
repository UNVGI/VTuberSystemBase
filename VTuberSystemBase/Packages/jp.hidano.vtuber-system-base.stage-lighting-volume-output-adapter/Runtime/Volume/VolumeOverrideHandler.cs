#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume
{
    /// <summary>
    /// Bridges <c>volume/override/{type}/enabled</c> and
    /// <c>volume/override/{type}/{param}</c> state topics to the live URP
    /// <c>VolumeProfile</c> exposed via <see cref="IOutputSceneRoots.GlobalVolumeProfile"/>.
    /// Also responds to <c>volume/override/schema</c> requests with the cached schema built
    /// at startup.
    /// </summary>
    internal sealed class VolumeOverrideHandler : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly IOutputSceneRoots _roots;
        private readonly IAdapterMessageSink _sink;
        private readonly AdapterErrorReporter _errorReporter;
        private readonly AdapterLogger _logger;
        private readonly StageLightingVolumeOutputAdapterDiagnostics _diagnostics;
        private readonly VolumeOverrideRegistry _registry = new();
        private readonly VolumeOverrideMetadataBuilder _metadataBuilder;
        private readonly HandlerRegistrationToken _tokens = new();
        private readonly HashSet<Type> _addedComponentTypes = new();

        private VolumeOverrideSchemaDto _cachedSchema;
        private bool _disposed;

        public VolumeOverrideHandler(
            IOutputCommandDispatcher dispatcher,
            IOutputSceneRoots roots,
            IAdapterMessageSink sink,
            AdapterErrorReporter errorReporter,
            AdapterLogger logger,
            StageLightingVolumeOutputAdapterDiagnostics diagnostics,
            VolumeOverrideMetadataBuilder? metadataBuilder = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _errorReporter = errorReporter ?? throw new ArgumentNullException(nameof(errorReporter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _metadataBuilder = metadataBuilder ?? new VolumeOverrideMetadataBuilder(_logger);
        }

        internal VolumeOverrideRegistry Registry => _registry;
        internal VolumeOverrideSchemaDto CachedSchema => _cachedSchema;

        public void Start(IReadOnlyList<Type>? volumeComponentTypes = null)
        {
            try
            {
                volumeComponentTypes ??= LoadVolumeComponentTypes();
                _registry.Build(volumeComponentTypes);
                _cachedSchema = _metadataBuilder.Build(volumeComponentTypes);
                _diagnostics.SetVolumeOverrideTypeCount(_registry.Count);

                int registeredHandlers = 0;
                foreach (var t in volumeComponentTypes)
                {
                    if (t == null || t.FullName == null) continue;
                    var typeFullName = t.FullName;

                    // enabled handler
                    _tokens.Add(_dispatcher.RegisterStateHandler<bool>(
                        StageLightingTopics.VolumeOverrideEnabled(typeFullName),
                        cmd => OnEnabled(typeFullName, cmd)));
                    registeredHandlers++;

                    // per-param handlers
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (!typeof(VolumeParameter).IsAssignableFrom(f.FieldType)) continue;
                        var paramName = f.Name;
                        _tokens.Add(_dispatcher.RegisterStateHandler<VolumeOverrideParamValueDto>(
                            StageLightingTopics.VolumeOverrideParam(typeFullName, paramName),
                            cmd => OnParam(typeFullName, paramName, cmd)));
                        registeredHandlers++;
                    }
                }

                // schema request handler.
                _tokens.Add(_dispatcher.RegisterRequestHandler<EmptyDto, VolumeOverrideSchemaDto>(
                    StageLightingTopics.VolumeOverrideSchema, _ => _cachedSchema));
                registeredHandlers++;
                _diagnostics.IncrementHandlerCount(registeredHandlers);
            }
            catch (Exception ex)
            {
                _logger.Error("VolumeOverrideHandler", "start_failed", context: ex.Message, exception: ex);
            }
        }

        private static IReadOnlyList<Type> LoadVolumeComponentTypes()
        {
            var arr = VolumeManager.instance.baseComponentTypeArray;
            return arr ?? Array.Empty<Type>();
        }

        private void OnEnabled(string typeFullName, StateCommand<bool> cmd)
        {
            try
            {
                if (!_registry.GetTypeByFullName(typeFullName, out var type))
                {
                    _logger.Warning("VolumeOverrideHandler", "unknown_type", context: "enabled",
                        topic: StageLightingTopics.VolumeOverrideEnabled(typeFullName), typeFullName: typeFullName);
                    return;
                }
                var profile = _roots.GlobalVolumeProfile;
                if (profile == null)
                {
                    _logger.Warning("VolumeOverrideHandler", "missing_profile", context: "enabled",
                        topic: StageLightingTopics.VolumeOverrideEnabled(typeFullName));
                    return;
                }
                if (!profile.TryGet(type, out VolumeComponent component))
                {
                    component = profile.Add(type, overrides: true);
                    _addedComponentTypes.Add(type);
                }
                component.active = cmd.Payload;
            }
            catch (Exception ex)
            {
                _logger.Error("VolumeOverrideHandler", "enabled_failed", context: ex.Message,
                    typeFullName: typeFullName, exception: ex);
            }
        }

        private void OnParam(string typeFullName, string paramName, StateCommand<VolumeOverrideParamValueDto> cmd)
        {
            try
            {
                if (!_registry.GetTypeByFullName(typeFullName, out var type))
                {
                    _logger.Warning("VolumeOverrideHandler", "unknown_type", context: "param",
                        topic: StageLightingTopics.VolumeOverrideParam(typeFullName, paramName),
                        typeFullName: typeFullName, paramName: paramName);
                    return;
                }
                var profile = _roots.GlobalVolumeProfile;
                if (profile == null) return;
                if (!profile.TryGet(type, out VolumeComponent component))
                {
                    component = profile.Add(type, overrides: true);
                    _addedComponentTypes.Add(type);
                }
                VolumeParameterReflectionSetter.ApplyValue(component, paramName, cmd.Payload, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error("VolumeOverrideHandler", "param_failed", context: ex.Message,
                    typeFullName: typeFullName, paramName: paramName, exception: ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _tokens.Dispose(); } catch { /* ignore */ }
            // Remove components added by this spec from the live profile.
            var profile = _roots.GlobalVolumeProfile;
            if (profile != null)
            {
                foreach (var t in _addedComponentTypes)
                {
                    try
                    {
                        if (profile.Has(t))
                        {
                            profile.Remove(t);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("VolumeOverrideHandler", "remove_failed",
                            context: ex.Message, typeFullName: t.FullName, exception: ex);
                    }
                }
            }
            _addedComponentTypes.Clear();
        }
    }
}
