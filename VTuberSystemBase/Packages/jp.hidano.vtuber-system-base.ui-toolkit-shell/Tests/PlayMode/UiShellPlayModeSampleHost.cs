#nullable enable
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;
using DiagLogLevel = VTuberSystemBase.UiToolkitShell.Diagnostics.LogLevel;

namespace VTuberSystemBase.UiToolkitShell.Tests.PlayMode
{
    /// <summary>
    /// Drives <see cref="UiShellLifecycleDriver"/> for the manual PlayMode verification scene
    /// (task 12.7, Requirement 10.4). Wires the supplied <see cref="UiToolkitShellSkinProfile"/>
    /// with a disconnected <see cref="FakeIpcClient"/> and a <see cref="FakeTabMountStrategy"/>
    /// so the operator can confirm — without spinning up real Addressables, real IPC transport,
    /// or any tab spec — that:
    /// <list type="number">
    ///   <item>Display 1 shows the root UIDocument with the tab bar (Requirement 1.1, 1.2).</item>
    ///   <item>The three tab buttons activate after preload completion (Requirement 3.3).</item>
    ///   <item>Clicking each button switches the active tab without freezing the main output
    ///         (Requirement 2.3, 2.9).</item>
    ///   <item>The notification bar region is rendered so disconnected-IPC fail-safes have a
    ///         place to surface (Requirements 9.1, 9.5, 9.6).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The script lives in the <c>UiToolkitShell.Tests</c> assembly so it can access
    /// <see cref="FakeIpcClient"/> / <see cref="FakeTabMountStrategy"/> /
    /// <see cref="FakeAddressablesInitializer"/>. The asmdef carries the
    /// <c>UNITY_INCLUDE_TESTS</c> define constraint, which is automatically set by Unity when
    /// the Test Framework package is enabled — no extra configuration is required to open the
    /// scene from the Test Runner window.
    /// </para>
    /// <para>
    /// <see cref="UiShellLifecycleDriver"/> auto-starts on
    /// <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c>; with no provider registered at
    /// that point the call is a silent no-op (the driver intentionally stays dormant for
    /// test runners that do not opt into the shell). <see cref="Awake"/> then registers the
    /// fake-backed config provider and explicitly starts the shell.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UiShellPlayModeSampleHost : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Default skin profile asset (Runtime.UxmlUss/DefaultSkinProfile.asset). " +
                 "RootVisualTreeAsset is required; tab UXML may be left null because the " +
                 "fake tab mount strategy synthesises in-memory tab roots for the sample.")]
        private UiToolkitShellSkinProfile? skinProfile;

        [SerializeField]
        [Tooltip("Active tab handed to TabBarController on startup.")]
        private TabId initialTab = TabId.Character;

        [SerializeField]
        [Tooltip("Minimum diagnostics log level: Debug surfaces every per-step entry; " +
                 "Info matches the production default.")]
        private DiagLogLevel minimumLogLevel = DiagLogLevel.Info;

        private void Awake()
        {
            if (skinProfile == null)
            {
                Debug.LogError(
                    $"{nameof(UiShellPlayModeSampleHost)}: SkinProfile is not assigned. " +
                    "Open UiShellPlayModeSample.unity in the Inspector and assign " +
                    "Runtime.UxmlUss/DefaultSkinProfile.asset.",
                    this);
                return;
            }

            // BeforeSceneLoad has already fired by the time Awake runs; with no provider
            // registered the auto-start was a silent no-op. Reset clears any stale provider
            // / start counters left behind by a previous PlayMode entry (the driver is
            // static and survives domain reloads inside the Editor).
            UiShellLifecycleDriver.ResetForTests();

            UiShellLifecycleDriver.Configure(configProvider: BuildConfig);
            UiShellLifecycleDriver.StartShell();
        }

        private void OnDestroy()
        {
            // PlayMode exit is also caught by UiShellLifecycleDriver.OnPlayModeStateChanged,
            // but tearing down here covers domain-reload mid-Play and scene swaps too.
            UiShellLifecycleDriver.StopShell();
            UiShellLifecycleDriver.ResetForTests();
        }

        private UiShellConfig BuildConfig()
        {
            // Bus stays in its default Disconnected state — Requirement 9.1 says the shell
            // must come up without waiting for IPC. Operators can flip the connection
            // state from a watch window if they want to drive the connection notification.
            var bus = new FakeIpcClient();

            // Synthesises three in-memory tab roots so SwitchTo can toggle style.display
            // without requiring a real per-tab UIDocument or any tab spec implementation.
            var tabMount = new FakeTabMountStrategy();

            // Ok-on-Immediate so the bootstrap path advances past
            // BootstrapStep.AddressablesInitialized without booting real Addressables —
            // no LoadAsync calls happen in the manual scene so this is sufficient.
            var addressables = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Ok(),
            };

            return new UiShellConfig
            {
                SkinProfile = skinProfile,
                IpcBus = bus,
                TabMountStrategy = tabMount,
                AddressablesInitializer = addressables,
                MinimumLogLevel = minimumLogLevel,
                InitialTab = initialTab,
            };
        }
    }
}
