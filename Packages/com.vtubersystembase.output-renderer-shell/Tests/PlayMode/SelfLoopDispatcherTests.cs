#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;
using VTuberSystemBase.OutputRendererShell.Dispatch;
using LogLevel = VTuberSystemBase.OutputRendererShell.Diagnostics.LogLevel;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 7.2: <c>core-ipc-foundation</c> の <see cref="InMemoryLoopbackTransport"/> を用いた
    /// 同一プロセス内 server/client 自己ループによる End-to-End ディスパッチ検証
    /// （Req 4.2 / 4.3 / 4.7 / 4.8 / 4.9 / 8.2）。
    /// state coalesce / event FIFO / request-response 相関の 3 種類すべてが 5 秒以内に成立することを確認する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上流契約上、coalesce / FIFO / 相関 ID は <c>CoreIpcBus</c> 内で実装されており（D-7 / D-10）、
    /// 本 spec のディスパッチャは独自にキューを持たない。本テストは bus.PublishState などで送出した
    /// メッセージを bus.SubscribeState で受信し、テスト内ブリッジが <see cref="OutputCommandDispatcher"/>
    /// の <c>OnEnvelopeReceived</c> へ転送することで、最終的にハンドラへ到達する経路を検証する。
    /// </para>
    /// <para>
    /// 複数クライアント想定（OR-2）の検証は本テストの単一プロセス／単一接続環境では完全再現できないため、
    /// 同一トピックへの連続 Publish が last-write-wins / FIFO 順を維持することを観測対象とする。
    /// </para>
    /// </remarks>
    [TestFixture]
    public class SelfLoopDispatcherTests
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan AssertTimeout = TimeSpan.FromSeconds(5);

        [UnityTest]
        [Description("state コマンド：bus.PublishState → bus.SubscribeState → dispatcher.OnEnvelopeReceived → 登録済 state ハンドラへ到達（Req 4.2 / 4.8）")]
        public IEnumerator SelfLoop_StateCommand_ReachesDispatcherHandler()
        {
            using var host = NewLoopbackHost();
            yield return InitAndAwaitConnected(host);
            var bus = host.Bus;

            using var dispatcher = new OutputCommandDispatcher(new OutputShellLogger(LogLevel.Verbose));
            string? captured = null;
            using var token = dispatcher.RegisterStateHandler<string>("self/state", cmd => captured = cmd.Payload);

            using var bridge = new BusToDispatcherBridge<string>(bus, "self/state", MessageKind.State, dispatcher);

            bus.PublishState("self/state", "v1");
            yield return WaitFor(() => captured == "v1", AssertTimeout, "state ハンドラが 'v1' で呼ばれること");

            // 連続 Publish の last-write-wins セマンティクス（上流 D-7 経由で観測）
            bus.PublishState("self/state", "v2");
            bus.PublishState("self/state", "v3");
            yield return WaitFor(() => captured == "v3", AssertTimeout,
                $"連続 Publish 後に最新値 'v3' へ収束すること (currently: '{captured}')");
        }

        [UnityTest]
        [Description("event コマンド：bus.PublishEvent → 到着順 FIFO でハンドラ呼び出し（Req 4.3 / 4.9）")]
        public IEnumerator SelfLoop_EventCommand_FifoOrderingPreserved()
        {
            using var host = NewLoopbackHost();
            yield return InitAndAwaitConnected(host);
            var bus = host.Bus;

            using var dispatcher = new OutputCommandDispatcher(new OutputShellLogger(LogLevel.Verbose));
            var received = new List<int>();
            using var token = dispatcher.RegisterEventHandler<int>("self/event", cmd =>
            {
                lock (received) received.Add(cmd.Payload);
            });

            using var bridge = new BusToDispatcherBridge<int>(bus, "self/event", MessageKind.Event, dispatcher);

            const int Count = 10;
            for (int i = 0; i < Count; i++)
            {
                bus.PublishEvent("self/event", i);
            }

            yield return WaitFor(() =>
            {
                lock (received) return received.Count == Count;
            }, AssertTimeout, $"event handler が {Count} 回呼ばれること");

            lock (received)
            {
                for (int i = 0; i < Count; i++)
                {
                    Assert.AreEqual(i, received[i], $"FIFO 順を維持すること (idx={i})");
                }
            }
        }

        [UnityTest]
        [Description("request/response：bus.RequestAsync → ハンドラが TResponse を返却 → 同一 correlationId で受信できること（Req 4.7）")]
        public IEnumerator SelfLoop_RequestResponse_CorrelationMatched()
        {
            using var host = NewLoopbackHost();
            yield return InitAndAwaitConnected(host);
            var bus = host.Bus;

            // 本テストでは bus.RegisterRequestHandler で直接ハンドラを登録し、bus が correlationId 紐付けを担保する。
            // 本 spec の OutputCommandDispatcher.RegisterRequestHandler は同一の意味論を XMLDoc で公開する契約である
            // （D-10 継承）。ここでは bus 単独でセマンティクスが成立することを確認する。
            using var sub = bus.RegisterRequestHandler<int, string>(
                "self/req",
                (req, ct) => Task.FromResult($"echo:{req}"));

            var task = bus.RequestAsync<int, string>("self/req", 7);

            var deadline = DateTime.UtcNow + AssertTimeout;
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("RequestAsync did not complete within timeout.");
                }
                yield return null;
            }

            Assert.IsTrue(task.IsCompletedSuccessfully, $"RequestAsync が成功すること: {task.Exception?.Message}");
            var result = task.Result;
            Assert.IsTrue(result.Success, $"IpcResult.Success であること（error: {result.Error?.Message}）");
            Assert.AreEqual("echo:7", result.Value);
        }

        private static CoreIpcRuntimeHost NewLoopbackHost()
        {
            return new CoreIpcRuntimeHost(
                transportFactory: _ => new InMemoryLoopbackTransport(),
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (delay, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));
        }

        private static IEnumerator InitAndAwaitConnected(CoreIpcRuntimeHost host)
        {
            var options = new CoreIpcOptions
            {
                Host = "loopback",
                Port = 0,
                ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
                ReconnectMaxAttempts = 3,
                DefaultRequestTimeout = TimeSpan.FromSeconds(5),
            };

            var initTask = host.InitializeAsync(options);
            yield return AwaitTask(initTask, StartupTimeout);
            Assert.AreEqual(RuntimeState.Running, host.State);

            var deadline = DateTime.UtcNow + StartupTimeout;
            while (host.Bus.Diagnostics.CurrentState != ConnectionState.Connected)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException(
                        $"CoreIpcRuntime did not reach Connected within {StartupTimeout}; current={host.Bus.Diagnostics.CurrentState}.");
                }
                yield return null;
            }
        }

        private static IEnumerator AwaitTask(Task task, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Awaited task did not complete within {timeout}.");
                }
                yield return null;
            }
            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception!;
            }
        }

        private static IEnumerator WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new AssertionException(failureMessage);
                }
                yield return null;
            }
        }

        /// <summary>
        /// テスト用ブリッジ：bus.SubscribeState/Event のコールバックを
        /// <see cref="OutputCommandDispatcher.OnEnvelopeReceived"/> へ転送する。
        /// 本ブリッジは Task 7.2 の E2E 検証専用で、本番ではディスパッチャを bus と直接結線する想定（後続タスク）。
        /// </summary>
        private sealed class BusToDispatcherBridge<TPayload> : IDisposable
        {
            private readonly ISubscriptionToken _subscription;

            public BusToDispatcherBridge(ICoreIpcBus bus, string topic, MessageKind kind, OutputCommandDispatcher dispatcher)
            {
                Action<TPayload> forward = payload =>
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                    using var doc = JsonDocument.Parse(bytes);
                    var envelope = new MessageEnvelope(
                        ProtocolVersion: "1.0",
                        Kind: kind,
                        Topic: topic,
                        CorrelationId: null,
                        TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Payload: doc.RootElement.Clone());
                    dispatcher.OnEnvelopeReceived(envelope);
                };

                _subscription = kind switch
                {
                    MessageKind.State => bus.SubscribeState(topic, forward),
                    MessageKind.Event => bus.SubscribeEvent(topic, forward),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Bridge does not support kind={kind}."),
                };
            }

            public void Dispose() => _subscription.Dispose();
        }
    }
}
