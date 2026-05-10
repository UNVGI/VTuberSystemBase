#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IUiCommandClient"/>. Records every send and lets
    /// the test inject the response for the next <c>RequestAsync</c> call. Mirrors
    /// the pattern used in <c>character-selection-tab</c>'s test doubles so that
    /// both tabs share the same shape.
    /// </summary>
    public sealed class FakeUiCommandClient : IUiCommandClient
    {
        public sealed class SendRecord
        {
            public string Topic { get; init; } = "";
            public MessageKind Kind { get; init; }
            public object? Payload { get; init; }
        }

        public sealed class RequestRecord
        {
            public string Topic { get; init; } = "";
            public object? Request { get; init; }
            public Type ResponseType { get; init; } = typeof(object);
            public TimeSpan? Timeout { get; init; }
        }

        public List<SendRecord> Sent { get; } = new List<SendRecord>();
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();

        public Func<RequestRecord, object?>? RequestResponder { get; set; }
        public bool ForceFail { get; set; }
        public SendError? FailWith { get; set; }

        public SendResult PublishState<TPayload>(string topic, TPayload payload)
        {
            Sent.Add(new SendRecord { Topic = topic, Kind = MessageKind.State, Payload = payload });
            return ForceFail
                ? SendResult.Fail(FailWith ?? new SendError(SendErrorCode.NotConnected))
                : SendResult.Ok();
        }

        public SendResult PublishEvent<TPayload>(string topic, TPayload payload)
        {
            Sent.Add(new SendRecord { Topic = topic, Kind = MessageKind.Event, Payload = payload });
            return ForceFail
                ? SendResult.Fail(FailWith ?? new SendError(SendErrorCode.NotConnected))
                : SendResult.Ok();
        }

        public Task<RequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var record = new RequestRecord
            {
                Topic = topic,
                Request = payload,
                ResponseType = typeof(TResponse),
                Timeout = timeout,
            };
            Requests.Add(record);
            if (RequestResponder is null)
            {
                return Task.FromResult(RequestResult<TResponse>.Fail(new RequestError(RequestErrorCode.Timeout)));
            }
            var resp = RequestResponder(record);
            if (resp is TResponse typed)
            {
                return Task.FromResult(RequestResult<TResponse>.Ok(typed));
            }
            return Task.FromResult(RequestResult<TResponse>.Fail(new RequestError(RequestErrorCode.SerializationFailed)));
        }
    }
}
