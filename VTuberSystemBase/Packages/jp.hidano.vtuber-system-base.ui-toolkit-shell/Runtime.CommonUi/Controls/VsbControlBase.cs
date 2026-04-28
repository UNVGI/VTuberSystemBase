#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.CommonUi.Controls
{
    /// <summary>
    /// Common base for all <c>vsb-</c> prefixed UI Toolkit custom controls
    /// (design.md §CommonUi §Shared Base Contract). Centralises the two
    /// shell-wide concerns the four common controls share:
    /// <list type="bullet">
    /// <item><description><see cref="DiagnosticsLogger"/> dependency injection
    /// (Requirement 11). The shell bootstrapper calls
    /// <see cref="SetDiagnosticsLogger"/> after a control is materialised from
    /// UXML by the default constructor.</description></item>
    /// <item><description><see cref="RegisterClassPrefix"/> applies the
    /// <c>vsb-{block}</c> BEM block class so all derived controls share a
    /// single source of truth for the <c>vsb-</c> prefix convention
    /// (<c>SkinValidationRules.Prefix</c>; Requirement 6.2, 7.3).</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Derived controls remain free to add element / modifier classes
    /// (<c>vsb-{block}__{element}</c>, <c>vsb-{block}--{modifier}</c>) on top.
    /// The base class deliberately does not own a layout: it inherits from
    /// <see cref="VisualElement"/> and lets each subclass build its own
    /// internal hierarchy. This keeps the abstract surface minimal so the
    /// asmdef remains a thin shared library (UI-4).
    /// </remarks>
    public abstract class VsbControlBase : VisualElement
    {
        /// <summary>
        /// Prefix applied to every shell-defined block class. Mirrors
        /// <c>SkinValidationRules.Prefix</c> intentionally to avoid forcing
        /// CommonUi to depend on the <c>Skin</c> namespace just to read a
        /// single string constant (the <c>SkinValidator</c> already references
        /// the same literal).
        /// </summary>
        public const string ClassPrefix = "vsb-";

        /// <summary>
        /// Logger handed to derived controls for emitting validation /
        /// interaction diagnostics. <c>null</c> when the control is created
        /// via the parameterless UXML factory ctor and the bootstrapper has
        /// not yet injected one; derived classes must therefore null-check
        /// before logging.
        /// </summary>
        protected IDiagnosticsLogger? DiagnosticsLogger { get; private set; }

        protected VsbControlBase(string blockName, IDiagnosticsLogger? diagnosticsLogger)
        {
            RegisterClassPrefix(blockName);
            DiagnosticsLogger = diagnosticsLogger;
        }

        /// <summary>
        /// Adds the <c>vsb-{blockName}</c> BEM block class to this element.
        /// Called once from each subclass constructor with its own block
        /// name (e.g. <c>"slider"</c>, <c>"color-picker"</c>).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if <paramref name="blockName"/> is null, empty, or already
        /// includes the <c>vsb-</c> prefix (which would produce a doubled
        /// prefix and silently break skin USS selectors).
        /// </exception>
        protected void RegisterClassPrefix(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName))
            {
                throw new ArgumentException(
                    "blockName must be a non-empty BEM block identifier.",
                    nameof(blockName));
            }

            if (blockName.StartsWith(ClassPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"blockName must not include the '{ClassPrefix}' prefix; it is added automatically.",
                    nameof(blockName));
            }

            AddToClassList(ClassPrefix + blockName);
        }

        /// <summary>
        /// Late-bound logger injection used when the control is constructed
        /// via the parameterless UXML factory path. The shell bootstrapper
        /// walks the panel after attach and calls this on every
        /// <see cref="VsbControlBase"/> descendant so that subsequent
        /// validation / interaction events surface in
        /// <see cref="LogCategory.Skin"/> just as if the control had been
        /// constructed in code.
        /// </summary>
        public void SetDiagnosticsLogger(IDiagnosticsLogger? logger)
        {
            DiagnosticsLogger = logger;
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Test-only accessor for the injected logger. Hidden from production
        /// callers via the <c>UNITY_INCLUDE_TESTS</c> guard so it cannot leak
        /// into shipping builds. <c>UiToolkitShell.Tests</c> reads this to
        /// verify dependency injection without exposing the protected field.
        /// </summary>
        public IDiagnosticsLogger? DiagnosticsLoggerForTests => DiagnosticsLogger;
#endif
    }
}
