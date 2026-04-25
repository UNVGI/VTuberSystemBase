#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class AbstractInterfaceContractTests
    {
        private static readonly Assembly AbstractionsAssembly = typeof(ICoreIpcBus).Assembly;

        private static readonly HashSet<string> AllowedExternalAssemblyPrefixes = new(StringComparer.Ordinal)
        {
            "System",
            "mscorlib",
            "netstandard",
            "Microsoft.Bcl",
        };

        [Test]
        public void AllAbstractContracts_AreDefinedInAbstractionsAssembly()
        {
            Type[] contracts =
            {
                typeof(ICoreIpcBus),
                typeof(ISubscriptionToken),
                typeof(ICoreIpcRuntime),
                typeof(ITransportAdapter),
                typeof(IClientConnection),
                typeof(IMessageCodec),
                typeof(IConnectionDiagnostics),
                typeof(IAuthenticationHandler),
                typeof(RequestOptions),
                typeof(ServerBindOptions),
                typeof(ClientBindOptions),
                typeof(DiagnosticsSnapshot),
                typeof(AuthenticationContext),
            };

            foreach (var t in contracts)
            {
                Assert.AreSame(
                    AbstractionsAssembly,
                    t.Assembly,
                    $"Type {t.FullName} must live in the Abstractions asmdef.");
                Assert.AreEqual(
                    "VTuberSystemBase.CoreIpc.Abstractions",
                    t.Namespace,
                    $"Type {t.FullName} must use the Abstractions namespace.");
            }
        }

        [Test]
        public void AbstractionsAssembly_HasNoForwardReferenceToNonStdAssemblies()
        {
            var referenced = AbstractionsAssembly.GetReferencedAssemblies();

            foreach (var name in referenced)
            {
                var simpleName = name.Name ?? string.Empty;
                var allowed = AllowedExternalAssemblyPrefixes.Any(prefix =>
                    simpleName.Equals(prefix, StringComparison.Ordinal)
                    || simpleName.StartsWith(prefix + ".", StringComparison.Ordinal));

                Assert.IsTrue(
                    allowed,
                    $"Abstractions asmdef must not reference '{simpleName}'. "
                    + "Only BCL / System.* / Microsoft.Bcl.* references are permitted "
                    + "to keep the abstraction layer free of concrete-type dependencies.");
            }
        }

        [Test]
        public void ICoreIpcBus_DeclaresAllRequiredMembers()
        {
            var t = typeof(ICoreIpcBus);

            AssertMethodExists(t, "PublishState", isGeneric: true, returnType: typeof(IpcResult));
            AssertMethodExists(t, "PublishEvent", isGeneric: true, returnType: typeof(IpcResult));
            AssertMethodExists(t, "RequestAsync", isGeneric: true);
            AssertMethodExists(t, "SubscribeState", isGeneric: true, returnType: typeof(ISubscriptionToken));
            AssertMethodExists(t, "SubscribeEvent", isGeneric: true, returnType: typeof(ISubscriptionToken));
            AssertMethodExists(t, "RegisterRequestHandler", isGeneric: true, returnType: typeof(ISubscriptionToken));

            var diagnostics = t.GetProperty(nameof(ICoreIpcBus.Diagnostics));
            Assert.IsNotNull(diagnostics, "ICoreIpcBus.Diagnostics property must exist.");
            Assert.AreEqual(typeof(IConnectionDiagnostics), diagnostics!.PropertyType);
        }

        [Test]
        public void ISubscriptionToken_ExtendsIDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(ISubscriptionToken)));
        }

        [Test]
        public void ICoreIpcRuntime_DeclaresLifecycleMembersAndExtendsIDisposable()
        {
            var t = typeof(ICoreIpcRuntime);

            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(t),
                "ICoreIpcRuntime must extend IDisposable for symmetric Dispose-based shutdown.");

            var state = t.GetProperty(nameof(ICoreIpcRuntime.State));
            Assert.IsNotNull(state);
            Assert.AreEqual(typeof(RuntimeState), state!.PropertyType);

            var bus = t.GetProperty(nameof(ICoreIpcRuntime.Bus));
            Assert.IsNotNull(bus);
            Assert.AreEqual(typeof(ICoreIpcBus), bus!.PropertyType);

            var options = t.GetProperty(nameof(ICoreIpcRuntime.Options));
            Assert.IsNotNull(options);
            Assert.AreEqual(typeof(CoreIpcOptions), options!.PropertyType);

            var initialize = t.GetMethod(nameof(ICoreIpcRuntime.InitializeAsync));
            Assert.IsNotNull(initialize);
            Assert.AreEqual(typeof(Task), initialize!.ReturnType);

            var parameters = initialize.GetParameters();
            Assert.AreEqual(2, parameters.Length);
            Assert.AreEqual(typeof(CoreIpcOptions), parameters[0].ParameterType);
            Assert.AreEqual(typeof(CancellationToken), parameters[1].ParameterType);
        }

        [Test]
        public void ITransportAdapter_DeclaresServerAndClientFactoriesAndIsAsyncDisposable()
        {
            var t = typeof(ITransportAdapter);

            Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(t),
                "ITransportAdapter must extend IAsyncDisposable for symmetric resource release.");

            var startServer = t.GetMethod(nameof(ITransportAdapter.StartServerAsync));
            Assert.IsNotNull(startServer);
            Assert.AreEqual(typeof(Task), startServer!.ReturnType);

            var startServerParams = startServer.GetParameters();
            Assert.AreEqual(typeof(ServerBindOptions), startServerParams[0].ParameterType);
            Assert.AreEqual(typeof(CancellationToken), startServerParams[1].ParameterType);

            var connectClient = t.GetMethod(nameof(ITransportAdapter.ConnectClientAsync));
            Assert.IsNotNull(connectClient);
            Assert.AreEqual(typeof(Task<IClientConnection>), connectClient!.ReturnType);

            var connectClientParams = connectClient.GetParameters();
            Assert.AreEqual(typeof(ClientBindOptions), connectClientParams[0].ParameterType);
            Assert.AreEqual(typeof(CancellationToken), connectClientParams[1].ParameterType);

            Assert.IsNotNull(t.GetEvent(nameof(ITransportAdapter.ClientConnected)));
            Assert.IsNotNull(t.GetEvent(nameof(ITransportAdapter.ClientDisconnected)));
        }

        [Test]
        public void IClientConnection_DeclaresSendReceiveAndIsAsyncDisposable()
        {
            var t = typeof(IClientConnection);

            Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(t));

            var send = t.GetMethod(nameof(IClientConnection.SendAsync));
            Assert.IsNotNull(send);
            Assert.AreEqual(typeof(ValueTask), send!.ReturnType);

            var sendParams = send.GetParameters();
            Assert.AreEqual(typeof(ReadOnlyMemory<byte>), sendParams[0].ParameterType);
            Assert.AreEqual(typeof(CancellationToken), sendParams[1].ParameterType);

            var receive = t.GetMethod(nameof(IClientConnection.ReceiveAsync));
            Assert.IsNotNull(receive);
            Assert.AreEqual(typeof(IAsyncEnumerable<ReadOnlyMemory<byte>>), receive!.ReturnType);

            var endpoint = t.GetProperty(nameof(IClientConnection.RemoteEndpoint));
            Assert.IsNotNull(endpoint);
            Assert.AreEqual(typeof(string), endpoint!.PropertyType);
        }

        [Test]
        public void IMessageCodec_DeclaresEncodeAndDecode()
        {
            var t = typeof(IMessageCodec);

            var encode = t.GetMethod(nameof(IMessageCodec.Encode));
            Assert.IsNotNull(encode);
            Assert.AreEqual(typeof(IpcResult<ReadOnlyMemory<byte>>), encode!.ReturnType);

            var decode = t.GetMethod(nameof(IMessageCodec.Decode));
            Assert.IsNotNull(decode);
            Assert.AreEqual(typeof(IpcResult<MessageEnvelope>), decode!.ReturnType);

            var decodeParams = decode.GetParameters();
            Assert.AreEqual(typeof(ReadOnlyMemory<byte>), decodeParams[0].ParameterType);
        }

        [Test]
        public void IConnectionDiagnostics_DeclaresAllPropertiesAndStateChangedEvent()
        {
            var t = typeof(IConnectionDiagnostics);

            AssertProperty(t, nameof(IConnectionDiagnostics.CurrentState), typeof(ConnectionState));
            AssertProperty(t, nameof(IConnectionDiagnostics.ReconnectAttemptCount), typeof(int));
            AssertProperty(t, nameof(IConnectionDiagnostics.PendingRequestCount), typeof(int));
            AssertProperty(t, nameof(IConnectionDiagnostics.StateSlotCount), typeof(int));
            AssertProperty(t, nameof(IConnectionDiagnostics.EventQueueCount), typeof(int));
            AssertProperty(t, nameof(IConnectionDiagnostics.ConnectedClientCount), typeof(int));

            var ev = t.GetEvent(nameof(IConnectionDiagnostics.ConnectionStateChanged));
            Assert.IsNotNull(ev, "ConnectionStateChanged event must exist.");
            Assert.AreEqual(typeof(Action<ConnectionState, ConnectionState>), ev!.EventHandlerType);

            var snap = t.GetMethod(nameof(IConnectionDiagnostics.TakeSnapshot));
            Assert.IsNotNull(snap);
            Assert.AreEqual(typeof(DiagnosticsSnapshot), snap!.ReturnType);
        }

        [Test]
        public void DiagnosticsSnapshot_PreservesConstructorArguments()
        {
            var taken = new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero);

            var snap = new DiagnosticsSnapshot(
                TakenAt: taken,
                ClientState: ConnectionState.Connected,
                ServerConnectedCount: 3,
                ReconnectAttemptCount: 1,
                PendingRequestCount: 2,
                StateSlotCount: 4,
                EventQueueCount: 5);

            Assert.AreEqual(taken, snap.TakenAt);
            Assert.AreEqual(ConnectionState.Connected, snap.ClientState);
            Assert.AreEqual(3, snap.ServerConnectedCount);
            Assert.AreEqual(1, snap.ReconnectAttemptCount);
            Assert.AreEqual(2, snap.PendingRequestCount);
            Assert.AreEqual(4, snap.StateSlotCount);
            Assert.AreEqual(5, snap.EventQueueCount);
        }

        [Test]
        public void RequestOptions_PreservesTimeout()
        {
            var options = new RequestOptions(TimeSpan.FromSeconds(7));

            Assert.AreEqual(TimeSpan.FromSeconds(7), options.Timeout);
        }

        [Test]
        public void ServerBindOptions_PreservesHostAndPort()
        {
            var options = new ServerBindOptions("127.0.0.1", 61874);

            Assert.AreEqual("127.0.0.1", options.Host);
            Assert.AreEqual(61874, options.Port);
        }

        [Test]
        public void ClientBindOptions_PreservesHostPortAndConnectTimeout()
        {
            var options = new ClientBindOptions("127.0.0.1", 61874, TimeSpan.FromSeconds(2));

            Assert.AreEqual("127.0.0.1", options.Host);
            Assert.AreEqual(61874, options.Port);
            Assert.AreEqual(TimeSpan.FromSeconds(2), options.ConnectTimeout);
        }

        [Test]
        public void IAuthenticationHandler_DeclaresAuthenticateAsync()
        {
            var t = typeof(IAuthenticationHandler);

            var auth = t.GetMethod(nameof(IAuthenticationHandler.AuthenticateAsync));
            Assert.IsNotNull(auth);
            Assert.AreEqual(typeof(Task<IpcResult>), auth!.ReturnType);
        }

        private static void AssertProperty(Type t, string name, Type propertyType)
        {
            var prop = t.GetProperty(name);
            Assert.IsNotNull(prop, $"{t.Name}.{name} property must exist.");
            Assert.AreEqual(propertyType, prop!.PropertyType);
        }

        private static void AssertMethodExists(
            Type t,
            string name,
            bool isGeneric = false,
            Type? returnType = null)
        {
            var method = t.GetMethods()
                .FirstOrDefault(m => m.Name == name && (!isGeneric || m.IsGenericMethodDefinition));

            Assert.IsNotNull(method, $"{t.Name}.{name} method must exist.");

            if (returnType != null)
            {
                Assert.AreEqual(returnType, method!.ReturnType,
                    $"{t.Name}.{name} return type mismatch.");
            }
        }
    }
}
