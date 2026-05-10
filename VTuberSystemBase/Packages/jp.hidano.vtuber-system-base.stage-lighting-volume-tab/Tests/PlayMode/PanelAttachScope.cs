#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// Unity 6 (UI Toolkit) 以降は <see cref="VisualElement"/> が <see cref="IPanel"/>
    /// に attach されていないと <c>BaseField&lt;T&gt;.value</c> setter からの
    /// <see cref="ChangeEvent{T}"/> や <c>style.backgroundImage</c> 反映が同期しない。
    /// テスト時に最小構成の <see cref="UIDocument"/> + <see cref="PanelSettings"/>
    /// を建てて要素を attach し、<c>Dispose()</c> で確実に破棄する。
    /// </summary>
    internal sealed class PanelAttachScope : IDisposable
    {
        private readonly GameObject _host;
        private readonly PanelSettings _panelSettings;
        private bool _disposed;

        public PanelAttachScope(VisualElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            _host = new GameObject("__VsbTestPanel") { hideFlags = HideFlags.HideAndDontSave };
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.hideFlags = HideFlags.HideAndDontSave;
            var doc = _host.AddComponent<UIDocument>();
            doc.panelSettings = _panelSettings;
            doc.rootVisualElement.Add(element);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnityEngine.Object.DestroyImmediate(_host);
            UnityEngine.Object.DestroyImmediate(_panelSettings);
        }
    }
}
