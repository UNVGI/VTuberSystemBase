#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// Test double for <see cref="IAddressablesInitializer"/>. Lets EditMode tests exercise
    /// <see cref="AddressablesBootstrap"/> without booting the real Addressables runtime
    /// (Requirement 10.7). Outcomes are programmable along two axes:
    /// <list type="bullet">
    ///   <item><see cref="CompletionMode"/>: <c>Immediate</c> resolves before
    ///         <see cref="InitializeAsync"/> returns; <c>Deferred</c> stores the callback
    ///         until the test calls <see cref="Resolve"/>.</item>
    ///   <item><see cref="StagedResult"/>: the result the fake will deliver. Defaults to
    ///         <see cref="AddressablesInitResult.Ok"/>.</item>
    /// </list>
    /// </summary>
    public sealed class FakeAddressablesInitializer : IAddressablesInitializer
    {
        public enum CompletionMode
        {
            Immediate,
            Deferred,
        }

        private Action<AddressablesInitResult>? _pendingCallback;

        public CompletionMode Mode { get; set; } = CompletionMode.Immediate;

        public AddressablesInitResult StagedResult { get; set; } = AddressablesInitResult.Ok();

        public int InvocationCount { get; private set; }

        public bool HasPendingCallback => _pendingCallback is not null;

        public void InitializeAsync(Action<AddressablesInitResult> onCompleted)
        {
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));

            InvocationCount++;

            if (Mode == CompletionMode.Immediate)
            {
                onCompleted(StagedResult);
                return;
            }

            _pendingCallback = onCompleted;
        }

        /// <summary>Resolves a deferred-mode initialization with <paramref name="result"/>.</summary>
        public void Resolve(AddressablesInitResult result)
        {
            var cb = _pendingCallback ?? throw new InvalidOperationException(
                "FakeAddressablesInitializer.Resolve called without a pending callback");
            _pendingCallback = null;
            cb(result);
        }
    }
}
