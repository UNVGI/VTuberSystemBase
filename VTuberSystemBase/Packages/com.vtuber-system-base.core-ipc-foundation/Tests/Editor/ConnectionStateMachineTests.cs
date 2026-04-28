#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Connection;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class ConnectionStateMachineTests
    {
        private static ConnectionStateMachine NewMachine(out List<string> warnings)
        {
            var captured = new List<string>();
            warnings = captured;
            return new ConnectionStateMachine(captured.Add);
        }

        private static List<(ConnectionState From, ConnectionState To)> RecordEvents(
            ConnectionStateMachine sm)
        {
            var captured = new List<(ConnectionState, ConnectionState)>();
            sm.StateChanged += (prev, next) => captured.Add((prev, next));
            return captured;
        }

        [Test]
        public void InitialState_IsDisconnected_AndShutdownNotRequested()
        {
            var sm = new ConnectionStateMachine();
            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsFalse(sm.ShutdownRequested);
        }

        // ---- 8 valid transition paths ----

        [Test]
        public void Path1_Disconnected_To_Connecting_Succeeds()
        {
            var sm = NewMachine(out _);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Connecting));

            Assert.AreEqual(ConnectionState.Connecting, sm.CurrentState);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Disconnected, ConnectionState.Connecting), events[0]);
        }

        [Test]
        public void Path2_Connecting_To_Connected_Succeeds()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Connected));

            Assert.AreEqual(ConnectionState.Connected, sm.CurrentState);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Connecting, ConnectionState.Connected), events[0]);
        }

        [Test]
        public void Path3_Connecting_To_Disconnected_OnHandshakeFailure_Succeeds()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Disconnected));

            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsFalse(sm.ShutdownRequested);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Connecting, ConnectionState.Disconnected), events[0]);
        }

        [Test]
        public void Path4_Connected_To_Reconnecting_OnSocketDrop_Succeeds()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Reconnecting));

            Assert.AreEqual(ConnectionState.Reconnecting, sm.CurrentState);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Connected, ConnectionState.Reconnecting), events[0]);
        }

        [Test]
        public void Path5_Reconnecting_To_Connecting_OnBackoffExpired_Succeeds()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Connecting));

            Assert.AreEqual(ConnectionState.Connecting, sm.CurrentState);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Reconnecting, ConnectionState.Connecting), events[0]);
        }

        [Test]
        public void Path6_Reconnecting_To_PermanentlyDisconnected_OnMaxAttempts_Succeeds()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.PermanentlyDisconnected));

            Assert.AreEqual(ConnectionState.PermanentlyDisconnected, sm.CurrentState);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(
                (ConnectionState.Reconnecting, ConnectionState.PermanentlyDisconnected),
                events[0]);
        }

        [Test]
        public void Path7_Connected_To_Disconnected_OnShutdown_SetsFlag()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Disconnected, isShutdown: true));

            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsTrue(sm.ShutdownRequested);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual((ConnectionState.Connected, ConnectionState.Disconnected), events[0]);
        }

        [Test]
        public void Path8_Reconnecting_To_Disconnected_OnShutdown_SetsFlag()
        {
            var sm = NewMachine(out _);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            var events = RecordEvents(sm);

            Assert.IsTrue(sm.TryTransition(ConnectionState.Disconnected, isShutdown: true));

            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsTrue(sm.ShutdownRequested);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(
                (ConnectionState.Reconnecting, ConnectionState.Disconnected),
                events[0]);
        }

        // ---- Shutdown suppression ----

        [Test]
        public void AfterShutdown_DisconnectedToConnecting_IsSuppressed_AndLogsWarning()
        {
            var sm = NewMachine(out var warnings);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Disconnected, isShutdown: true);
            var events = RecordEvents(sm);
            warnings.Clear();

            var result = sm.TryTransition(ConnectionState.Connecting);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsTrue(sm.ShutdownRequested);
            Assert.AreEqual(0, events.Count, "no event should fire on suppressed transition");
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Connecting", warnings[0]);
            StringAssert.Contains("shutdown", warnings[0]);
        }

        [Test]
        public void Shutdown_FromDisconnected_OnlySetsFlag_AndDoesNotFireEvent()
        {
            var sm = NewMachine(out _);
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Disconnected, isShutdown: true);

            Assert.IsFalse(result, "no state change so TryTransition returns false");
            Assert.IsTrue(sm.ShutdownRequested);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void Shutdown_TargetingNonDisconnected_IsRejected_AndLogsWarning()
        {
            var sm = NewMachine(out var warnings);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            warnings.Clear();
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Reconnecting, isShutdown: true);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.Connected, sm.CurrentState);
            Assert.IsFalse(sm.ShutdownRequested);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("shutdown", warnings[0]);
        }

        // ---- Invalid transitions ----

        [Test]
        public void InvalidTransition_DisconnectedToConnected_IsIgnored_AndLogsWarning()
        {
            var sm = NewMachine(out var warnings);
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Connected);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("invalid transition", warnings[0]);
            StringAssert.Contains("Disconnected", warnings[0]);
            StringAssert.Contains("Connected", warnings[0]);
        }

        [Test]
        public void InvalidTransition_ConnectedToConnecting_IsIgnored_AndLogsWarning()
        {
            var sm = NewMachine(out var warnings);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            warnings.Clear();
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Connecting);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.Connected, sm.CurrentState);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("invalid transition", warnings[0]);
        }

        [Test]
        public void InvalidTransition_PermanentlyDisconnectedToConnecting_IsIgnored()
        {
            var sm = NewMachine(out var warnings);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            sm.TryTransition(ConnectionState.PermanentlyDisconnected);
            warnings.Clear();
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Connecting);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.PermanentlyDisconnected, sm.CurrentState);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void InvalidTransition_DoesNotThrow()
        {
            var sm = new ConnectionStateMachine();

            Assert.DoesNotThrow(() => sm.TryTransition(ConnectionState.Connected));
            Assert.DoesNotThrow(() => sm.TryTransition(ConnectionState.Reconnecting));
            Assert.DoesNotThrow(() => sm.TryTransition(ConnectionState.PermanentlyDisconnected));
        }

        // ---- No-op transitions ----

        [Test]
        public void SameStateTransition_IsNoOp_NoEvent_NoWarning()
        {
            var sm = NewMachine(out var warnings);
            sm.TryTransition(ConnectionState.Connecting);
            warnings.Clear();
            var events = RecordEvents(sm);

            var result = sm.TryTransition(ConnectionState.Connecting);

            Assert.IsFalse(result);
            Assert.AreEqual(ConnectionState.Connecting, sm.CurrentState);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, warnings.Count, "same-state transitions should be silent no-ops");
        }

        // ---- Event semantics ----

        [Test]
        public void StateChanged_FiresExactlyOncePerTransition()
        {
            var sm = new ConnectionStateMachine();
            int count = 0;
            sm.StateChanged += (_, _) => count++;

            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            sm.TryTransition(ConnectionState.PermanentlyDisconnected);

            Assert.AreEqual(4, count);
        }

        [Test]
        public void StateChanged_DeliversPreviousAndCurrentInOrder()
        {
            var sm = new ConnectionStateMachine();
            var captured = RecordEvents(sm);

            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            sm.TryTransition(ConnectionState.Connecting);

            Assert.AreEqual(4, captured.Count);
            Assert.AreEqual((ConnectionState.Disconnected, ConnectionState.Connecting), captured[0]);
            Assert.AreEqual((ConnectionState.Connecting, ConnectionState.Connected), captured[1]);
            Assert.AreEqual((ConnectionState.Connected, ConnectionState.Reconnecting), captured[2]);
            Assert.AreEqual((ConnectionState.Reconnecting, ConnectionState.Connecting), captured[3]);
        }

        [Test]
        public void StateChanged_MultipleSubscribers_AllReceiveTransition()
        {
            var sm = new ConnectionStateMachine();
            int countA = 0, countB = 0;
            sm.StateChanged += (_, _) => countA++;
            sm.StateChanged += (_, _) => countB++;

            sm.TryTransition(ConnectionState.Connecting);

            Assert.AreEqual(1, countA);
            Assert.AreEqual(1, countB);
        }

        // ---- Full reconnect scenario ----

        [Test]
        public void FullReconnectScenario_DropAndRecover_FiresExpectedSequence()
        {
            var sm = new ConnectionStateMachine();
            var captured = RecordEvents(sm);

            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);
            sm.TryTransition(ConnectionState.Reconnecting);
            sm.TryTransition(ConnectionState.Connecting);
            sm.TryTransition(ConnectionState.Connected);

            CollectionAssert.AreEqual(
                new[]
                {
                    (ConnectionState.Disconnected, ConnectionState.Connecting),
                    (ConnectionState.Connecting, ConnectionState.Connected),
                    (ConnectionState.Connected, ConnectionState.Reconnecting),
                    (ConnectionState.Reconnecting, ConnectionState.Connecting),
                    (ConnectionState.Connecting, ConnectionState.Connected),
                },
                captured);
            Assert.IsFalse(sm.ShutdownRequested);
        }
    }
}
