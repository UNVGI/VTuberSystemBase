#nullable enable
using UnityEngine;

namespace VTuberSystemBase.OutputRendererShell.Display
{
    /// <summary>
    /// 本番用 <see cref="IDisplayProbe"/> 実装。<c>Display.displays</c> および <c>Screen.fullScreenMode</c> を直接参照する。
    /// </summary>
    public sealed class UnityDisplayProbe : IDisplayProbe
    {
        /// <inheritdoc />
        public int DisplayCount => UnityEngine.Display.displays.Length;

        /// <inheritdoc />
        public bool IsEditor => Application.isEditor;

        /// <inheritdoc />
        public void ActivateDisplay(int index)
        {
            if (index < 0 || index >= UnityEngine.Display.displays.Length) return;
            // Display.displays[0] は Activate 不可（自動）。1 以上のみ Activate を呼ぶ。
            if (index == 0) return;
            UnityEngine.Display.displays[index].Activate();
        }

        /// <inheritdoc />
        public void SetFullScreenMode(FullScreenMode mode)
        {
            if (Application.isEditor) return;
            Screen.fullScreenMode = mode;
        }
    }
}
