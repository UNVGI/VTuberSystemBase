#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;

namespace VTuberSystemBase.CoreIpc.Samples.MinimalLoopback
{
    public sealed class MinimalLoopbackDemo : MonoBehaviour
    {
        public sealed class Greeting
        {
            public int Counter { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        [SerializeField] private string stateTopic = "demo/state";
        [SerializeField] private string eventTopic = "demo/event";
        [SerializeField] private string requestTopic = "demo/echo";
        [SerializeField] private float publishIntervalSeconds = 1.0f;
        [SerializeField] private float runtimeWaitTimeoutSeconds = 10.0f;

        private ISubscriptionToken? _stateToken;
        private ISubscriptionToken? _eventToken;
        private ISubscriptionToken? _requestToken;
        private float _nextPublishAt;
        private float _wireDeadline;
        private int _counter;
        private bool _wired;
        private bool _giveUp;

        private void OnEnable()
        {
            float now = Time.realtimeSinceStartup;
            _nextPublishAt = now + publishIntervalSeconds;
            _wireDeadline = now + runtimeWaitTimeoutSeconds;
        }

        private void OnDisable()
        {
            ReleaseSubscriptions();
        }

        private void Update()
        {
            if (_giveUp) return;

            if (!_wired)
            {
                if (!TryWireSubscriptions(out var reason))
                {
                    if (Time.realtimeSinceStartup > _wireDeadline)
                    {
                        _giveUp = true;
                        Debug.LogError(
                            $"[MinimalLoopback] CoreIpcRuntime did not reach Running state within {runtimeWaitTimeoutSeconds}s ({reason}). " +
                            "Verify that RuntimeBootstrap auto-initialization succeeded and that no other process is bound to the configured port.");
                    }
                    return;
                }
                _wired = true;
            }

            if (Time.realtimeSinceStartup < _nextPublishAt) return;
            _nextPublishAt = Time.realtimeSinceStartup + publishIntervalSeconds;
            _counter++;
            PublishStateAndEvent();
            _ = IssueRequestAsync(_counter);
        }

        private bool TryWireSubscriptions(out string failureReason)
        {
            var runtime = CoreIpcRuntime.Current;
            if (runtime is null)
            {
                failureReason = "CoreIpcRuntime.Current is null";
                return false;
            }
            if (runtime.State != RuntimeState.Running)
            {
                failureReason = $"CoreIpcRuntime.State == {runtime.State}";
                return false;
            }

            var bus = runtime.Bus;

            _stateToken = bus.SubscribeState<Greeting>(
                stateTopic,
                payload => Debug.Log(
                    $"[MinimalLoopback][state:{stateTopic}] received Counter={payload.Counter} Message='{payload.Message}'"));

            _eventToken = bus.SubscribeEvent<Greeting>(
                eventTopic,
                payload => Debug.Log(
                    $"[MinimalLoopback][event:{eventTopic}] received Counter={payload.Counter} Message='{payload.Message}'"));

            _requestToken = bus.RegisterRequestHandler<Greeting, Greeting>(
                requestTopic,
                (req, _) => Task.FromResult(new Greeting
                {
                    Counter = req.Counter,
                    Message = "echo:" + req.Message,
                }));

            Debug.Log(
                $"[MinimalLoopback] subscriptions wired (state={stateTopic}, event={eventTopic}, request={requestTopic}); " +
                $"endpoint=ws://{runtime.Options.Host}:{runtime.Options.Port}");

            failureReason = string.Empty;
            return true;
        }

        private void PublishStateAndEvent()
        {
            var runtime = CoreIpcRuntime.Current;
            if (runtime is null) return;
            var bus = runtime.Bus;

            var stateResult = bus.PublishState(stateTopic, new Greeting
            {
                Counter = _counter,
                Message = $"state-{_counter}",
            });
            if (!stateResult.Success)
            {
                Debug.LogWarning(
                    $"[MinimalLoopback] PublishState failed (counter={_counter}): {stateResult.Error}");
            }

            var eventResult = bus.PublishEvent(eventTopic, new Greeting
            {
                Counter = _counter,
                Message = $"event-{_counter}",
            });
            if (!eventResult.Success)
            {
                Debug.LogWarning(
                    $"[MinimalLoopback] PublishEvent failed (counter={_counter}): {eventResult.Error}");
            }
        }

        private async Task IssueRequestAsync(int counter)
        {
            var runtime = CoreIpcRuntime.Current;
            if (runtime is null) return;

            try
            {
                var result = await runtime.Bus.RequestAsync<Greeting, Greeting>(
                    requestTopic,
                    new Greeting { Counter = counter, Message = $"request-{counter}" });
                if (result.Success)
                {
                    Debug.Log(
                        $"[MinimalLoopback][request:{requestTopic}] response Counter={result.Value!.Counter} Message='{result.Value!.Message}'");
                }
                else
                {
                    Debug.LogWarning(
                        $"[MinimalLoopback][request:{requestTopic}] failed (counter={counter}): {result.Error}");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[MinimalLoopback][request:{requestTopic}] threw (counter={counter}): {ex}");
            }
        }

        private void ReleaseSubscriptions()
        {
            _stateToken?.Dispose();
            _eventToken?.Dispose();
            _requestToken?.Dispose();
            _stateToken = null;
            _eventToken = null;
            _requestToken = null;
            _wired = false;
        }
    }
}
