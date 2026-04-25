#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// Test double for <see cref="ITabMountStrategy"/>. By default mounts the three canonical
    /// tabs by creating an in-memory <see cref="VisualElement"/> per tab and binding it via
    /// <see cref="ITabPanelRegistry.NotifyTabMounted(TabId, VisualElement)"/>. Tests can flip
    /// <see cref="ShouldThrow"/> / <see cref="ReturnFalse"/> to drive the
    /// <see cref="BootstrapErrorCode.TabUxmlAttachFailed"/> path.
    /// </summary>
    public sealed class FakeTabMountStrategy : ITabMountStrategy
    {
        public Dictionary<TabId, VisualElement> CreatedRoots { get; } = new Dictionary<TabId, VisualElement>();
        public bool ShouldThrow { get; set; }
        public Exception? ThrowException { get; set; }
        public bool ReturnFalse { get; set; }
        public int InvocationCount { get; private set; }
        public TabMountContext? LastContext { get; private set; }

        public bool MountTabs(TabMountContext context)
        {
            InvocationCount++;
            LastContext = context;

            if (ShouldThrow)
            {
                throw ThrowException ?? new InvalidOperationException("FakeTabMountStrategy configured to throw");
            }

            if (ReturnFalse)
            {
                return false;
            }

            foreach (TabId tabId in new[] { TabId.Character, TabId.StageLighting, TabId.CameraSwitcher })
            {
                var element = new VisualElement { name = $"fake-tab-{tabId}" };
                element.AddToClassList("vsb-tab-root");
                CreatedRoots[tabId] = element;
                context.RootVisualElement.Add(element);
                context.Registry.NotifyTabMounted(tabId, element);
            }

            return true;
        }
    }
}
