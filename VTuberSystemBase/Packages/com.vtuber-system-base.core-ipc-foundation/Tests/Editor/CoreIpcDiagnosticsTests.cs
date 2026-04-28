#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Connection;
using VTuberSystemBase.CoreIpc.Core.Diagnostics;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcDiagnosticsTests
    {
        [Test]
        public void NullStateMachine_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CoreIpcDiagnostics(null!));
        }

        [Test]
        public void DefaultProviders_ReturnZero()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            Assert.AreEqual(0, diag.ReconnectAttemptCount);
            Assert.AreEqual(0, diag.PendingRequestCount);
            Assert.AreEqual(0, diag.StateSlotCount);
            Assert.AreEqual(0, diag.EventQueueCount);
            Assert.AreEqual(0, diag.ConnectedClientCount);
        }

        [Test]
        public void CurrentState_ReflectsStateMachine()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            Assert.AreEqual(ConnectionState.Disconnected, diag.CurrentState);

            sm.TryTransition(ConnectionState.Connecting);
            Assert.AreEqual(ConnectionState.Connecting, diag.CurrentState);

            sm.TryTransition(ConnectionState.Connected);
            Assert.AreEqual(ConnectionState.Connected, diag.CurrentState);
        }

        [Test]
        public void LiveProviders_AreInvokedEachAccess()
        {
            int reconnect = 0, pending = 0, slots = 0, events = 0, clients = 0;
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(
                sm,
                reconnectAttemptCountProvider: () => reconnect,
                pendingRequestCountProvider: () => pending,
                stateSlotCountProvider: () => slots,
                eventQueueCountProvider: () => events,
                connectedClientCountProvider: () => clients);

            reconnect = 3;
            pending = 5;
            slots = 7;
            events = 11;
            clients = 2;

            Assert.AreEqual(3, diag.ReconnectAttemptCount);
            Assert.AreEqual(5, diag.PendingRequestCount);
            Assert.AreEqual(7, diag.StateSlotCount);
            Assert.AreEqual(11, diag.EventQueueCount);
            Assert.AreEqual(2, diag.ConnectedClientCount);

            reconnect = 4;
            pending = 6;
            Assert.AreEqual(4, diag.ReconnectAttemptCount, "Provider must be re-invoked on each access (live values).");
            Assert.AreEqual(6, diag.PendingRequestCount);
        }

        [Test]
        public void ConnectionStateChanged_FiresOncePerTransition()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            var captured = new List<(ConnectionState From, ConnectionState To)>();
            diag.ConnectionStateChanged += (prev, curr) => captured.Add((prev, curr));

            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);

            Assert.AreEqual(5, captured.Count, "Each successful transition must fire exactly once.");
            Assert.AreEqual((ConnectionState.Disconnected, ConnectionState.Connecting), captured[0]);
            Assert.AreEqual((ConnectionState.Connecting, ConnectionState.Connected), captured[1]);
            Assert.AreEqual((ConnectionState.Connected, ConnectionState.Reconnecting), captured[2]);
            Assert.AreEqual((ConnectionState.Reconnecting, ConnectionState.Connecting), captured[3]);
            Assert.AreEqual((ConnectionState.Connecting, ConnectionState.Connected), captured[4]);
        }

        [Test]
        public void ConnectionStateChanged_DoesNotFireForNoOpTransition()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            int count = 0;
            diag.ConnectionStateChanged += (_, _) => count++;

            sm.TryTransition(ConnectionState.Disconnected);

            Assert.AreEqual(0, count, "Same-state no-op transition must not fire ConnectionStateChanged.");
        }

        [Test]
        public void ConnectionStateChanged_DoesNotFireForInvalidTransition()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            int count = 0;
            diag.ConnectionStateChanged += (_, _) => count++;

            Assert.IsFalse(sm.TryTransition(ConnectionState.Connected),
                "Disconnected -> Connected is not a valid direct transition.");
            Assert.AreEqual(0, count, "Invalid transitions must not fire ConnectionStateChanged.");
        }

        [Test]
        public void ConnectionStateChanged_MultipleSubscribers_AllReceiveEachEvent()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            int a = 0, b = 0;
            diag.ConnectionStateChanged += (_, _) => a++;
            diag.ConnectionStateChanged += (_, _) => b++;

            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);

            Assert.AreEqual(2, a);
            Assert.AreEqual(2, b);
        }

        [Test]
        public void TakeSnapshot_ReturnsConsistentValues()
        {
            int reconnect = 9, pending = 4, slots = 2, events = 6, clients = 3;
            var sm = new ConnectionStateMachine();
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);

            var fixedNow = new DateTimeOffset(2026, 4, 25, 10, 0, 0, TimeSpan.Zero);
            using var diag = new CoreIpcDiagnostics(
                sm,
                reconnectAttemptCountProvider: () => reconnect,
                pendingRequestCountProvider: () => pending,
                stateSlotCountProvider: () => slots,
                eventQueueCountProvider: () => events,
                connectedClientCountProvider: () => clients,
                nowProvider: () => fixedNow);

            DiagnosticsSnapshot snap = diag.TakeSnapshot();

            Assert.AreEqual(fixedNow, snap.TakenAt);
            Assert.AreEqual(ConnectionState.Connected, snap.ClientState);
            Assert.AreEqual(3, snap.ServerConnectedCount);
            Assert.AreEqual(9, snap.ReconnectAttemptCount);
            Assert.AreEqual(4, snap.PendingRequestCount);
            Assert.AreEqual(2, snap.StateSlotCount);
            Assert.AreEqual(6, snap.EventQueueCount);
        }

        [Test]
        public void TakeSnapshot_IsImmutableValueRecord()
        {
            int reconnect = 1;
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(
                sm,
                reconnectAttemptCountProvider: () => reconnect);

            var snap1 = diag.TakeSnapshot();

            reconnect = 100;

            Assert.AreEqual(1, snap1.ReconnectAttemptCount,
                "Snapshot must be a frozen point-in-time value, unaffected by later provider state changes.");
        }

        [Test]
        public void TakeSnapshot_DefaultNow_IsRecent()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            var before = DateTimeOffset.UtcNow.AddSeconds(-1);
            var snap = diag.TakeSnapshot();
            var after = DateTimeOffset.UtcNow.AddSeconds(1);

            Assert.IsTrue(snap.TakenAt >= before && snap.TakenAt <= after,
                $"Default TakenAt should be near UtcNow; got {snap.TakenAt} (window {before}..{after}).");
        }

        [Test]
        public void Dispose_UnsubscribesFromStateMachine()
        {
            var sm = new ConnectionStateMachine();
            var diag = new CoreIpcDiagnostics(sm);

            int count = 0;
            diag.ConnectionStateChanged += (_, _) => count++;

            sm.TryTransition(ConnectionState.Connecting);
            Assert.AreEqual(1, count);

            diag.Dispose();

            sm.TryTransition(ConnectionState.Connected);
            Assert.AreEqual(1, count,
                "After Dispose, transitions on the source state machine must not propagate to ConnectionStateChanged.");
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var sm = new ConnectionStateMachine();
            var diag = new CoreIpcDiagnostics(sm);
            diag.Dispose();
            Assert.DoesNotThrow(() => diag.Dispose(),
                "Dispose() must be safe to invoke multiple times.");
        }

        [Test]
        public void ImplementsIConnectionDiagnostics()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);

            Assert.IsInstanceOf<IConnectionDiagnostics>(diag);
        }

        [Test]
        public void IConnectionDiagnosticsEvent_IsBackedByStateMachine()
        {
            var sm = new ConnectionStateMachine();
            using var diag = new CoreIpcDiagnostics(sm);
            IConnectionDiagnostics asInterface = diag;

            int count = 0;
            ConnectionState fromCaptured = ConnectionState.Disconnected;
            ConnectionState toCaptured = ConnectionState.Disconnected;
            asInterface.ConnectionStateChanged += (from, to) =>
            {
                fromCaptured = from;
                toCaptured = to;
                count++;
            };

            sm.TryTransition(ConnectionState.Connecting);

            Assert.AreEqual(1, count);
            Assert.AreEqual(ConnectionState.Disconnected, fromCaptured);
            Assert.AreEqual(ConnectionState.Connecting, toCaptured);
        }
    }
}
