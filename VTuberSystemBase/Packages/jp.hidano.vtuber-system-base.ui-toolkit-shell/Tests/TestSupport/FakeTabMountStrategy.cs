#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

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

        /// <summary>
        /// Tab IDs whose root <see cref="VisualElement"/> should be created without the
        /// per-tab modifier class (e.g. <c>vsb-tab-root--character</c>). Used by skin
        /// integration tests (task 11.1) to drive the "missing required class → tab marked
        /// failed" path through <see cref="SkinValidator"/>.
        /// </summary>
        public HashSet<TabId> OmitModifierClassFor { get; } = new HashSet<TabId>();

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
                element.AddToClassList(SkinValidationRules.CharacterTab.TabRoot);
                if (!OmitModifierClassFor.Contains(tabId))
                {
                    element.AddToClassList(ModifierClassFor(tabId));
                }
                CreatedRoots[tabId] = element;
                context.RootVisualElement.Add(element);
                context.Registry.NotifyTabMounted(tabId, element);
            }

            return true;
        }

        private static string ModifierClassFor(TabId tabId)
        {
            return tabId switch
            {
                TabId.Character => SkinValidationRules.CharacterTab.TabRootModifier,
                TabId.StageLighting => SkinValidationRules.StageLightingTab.TabRootModifier,
                TabId.CameraSwitcher => SkinValidationRules.CameraSwitcherTab.TabRootModifier,
                _ => throw new ArgumentOutOfRangeException(nameof(tabId), tabId, null),
            };
        }
    }
}
