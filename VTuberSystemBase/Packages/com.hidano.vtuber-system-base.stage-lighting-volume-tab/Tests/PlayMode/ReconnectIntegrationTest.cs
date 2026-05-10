#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// Reconnection + tab-lifecycle integration tests (Task 8.3, Requirements 9.8, 9.9,
    /// 11.4, 12.5). Verifies the ViewModel re-fetches the schema on connect-recover,
    /// and that repeated activate/deactivate cycles do not leak subscriptions.
    /// </summary>
    [TestFixture]
    public sealed class ReconnectIntegrationTest
    {
        private static (StageLightingVolumeTabViewModel vm,
                        FakeIpcClient ipc,
                        FakeConnectionStatus conn,
                        FakeClock clock) Build()
        {
            var ipc = new FakeIpcClient();
            ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());
            var clock = new FakeClock();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            var logger = new FakeDiagnosticsLogger();
            var lightList = new LightListState(ipc, logger);
            var stageCatalog = new StageCatalogState(ipc, logger);
            var volumeCache = new VolumeSchemaCache(ipc, logger);
            var debounce = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);
            var diag = new StageTabDiagnostics(logger);
            var vm = new StageLightingVolumeTabViewModel(
                ipc, ipc, conn, new FakePresetStorage(),
                lightList, stageCatalog, volumeCache, debounce, clock, diag, logger);
            return (vm, ipc, conn, clock);
        }

        [Test]
        public async Task ReconnectAfterDisconnect_FetchesSchemaOnRecovery()
        {
            var (vm, ipc, conn, _) = Build();
            vm.OnActivated();
            // No requests made while disconnected.
            Assert.That(ipc.Requests, Is.Empty);

            conn.SetStatus(ConnectionStatusCode.Connected);
            await Task.Delay(40);

            Assert.That(ipc.Requests, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void TabSwitchLifecycle_ActivateDeactivateRepeat_DoesNotLeakSubscriptions()
        {
            var (vm, ipc, conn, _) = Build();
            conn.SetStatus(ConnectionStatusCode.Connected);

            for (int i = 0; i < 5; i++)
            {
                vm.OnActivated();
                Assert.That(ipc.Subscriptions.Count, Is.GreaterThan(0));
                vm.OnDeactivated();
                Assert.That(ipc.Subscriptions, Is.Empty,
                    $"Iteration {i}: subscriptions should be drained on deactivate.");
            }

            vm.Dispose();
            Assert.That(ipc.Subscriptions, Is.Empty);
        }
    }
}
