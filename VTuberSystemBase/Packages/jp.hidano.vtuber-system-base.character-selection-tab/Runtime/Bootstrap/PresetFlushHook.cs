#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VTuberSystemBase.CharacterSelectionTab.Bootstrap
{
    /// <summary>
    /// Idempotent flush hook for <see cref="IPresetStoreLogic"/>. (task 6.2.)
    /// Wires <see cref="Application.quitting"/> in standalone and the editor
    /// <c>playModeStateChanged == ExitingPlayMode</c> transition so unsaved
    /// preset edits are persisted when the host shuts down (Req 8.4).
    /// </summary>
    public sealed class PresetFlushHook : IDisposable
    {
        private readonly IPresetStoreLogic _presets;
        private readonly IDiagnosticsLogger? _log;
        private bool _disposed;
        private bool _flushedOnce;

        public PresetFlushHook(IPresetStoreLogic presets, IDiagnosticsLogger? logger = null)
        {
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _log = logger;
            Application.quitting += OnAppQuitting;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        /// <summary>Force a flush immediately (used by tests and shutdown paths).</summary>
        public Task FlushNowAsync()
        {
            _flushedOnce = true;
            return _presets.FlushPendingAsync();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.quitting -= OnAppQuitting;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

        private void OnAppQuitting()
        {
            try { FlushNowAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"PresetFlushHook.OnAppQuitting failed: {ex.Message}");
            }
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            try { FlushNowAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"PresetFlushHook.OnPlayModeStateChanged failed: {ex.Message}");
            }
        }
#endif
    }
}
