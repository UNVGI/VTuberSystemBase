using System;
using RealtimeAvatarController.Core;
using UniRx;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Domain;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Senders
{
    /// <summary>
    /// RAC <see cref="ISlotErrorChannel"/> を購読し、<c>SlotError</c> を <c>slot/{slotId}/error</c> event に翻訳して発行する
    /// （Requirement 7.1〜7.7, 2.5, 2.6）。
    /// </summary>
    internal sealed class SlotErrorTranslator : IDisposable
    {
        private readonly IAdapterMessageSink _sink;
        private readonly ISlotErrorChannel _errorChannel;
        private readonly SlotStatusPublisher _statusPublisher;
        private readonly RacMainOutputAdapterConfig _config;
        private readonly IDiagnosticsLogger _logger;

        private IDisposable _subscription;

        /// <summary>最後に publish したエラーの UnixMs（診断用、未発生時 0）。</summary>
        public long LastErrorAtUnixMs { get; private set; }

        /// <summary>最後に publish したエラーメッセージ（診断用、未発生時 string.Empty）。</summary>
        public string LastErrorMessage { get; private set; } = string.Empty;

        /// <summary>本 translator を生成する。</summary>
        public SlotErrorTranslator(
            IAdapterMessageSink sink,
            ISlotErrorChannel errorChannel,
            SlotStatusPublisher statusPublisher,
            RacMainOutputAdapterConfig config,
            IDiagnosticsLogger logger)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _errorChannel = errorChannel ?? throw new ArgumentNullException(nameof(errorChannel));
            _statusPublisher = statusPublisher ?? throw new ArgumentNullException(nameof(statusPublisher));
            _config = config ?? new RacMainOutputAdapterConfig();
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary><see cref="ISlotErrorChannel.Errors"/> をメインスレッド経由で購読開始する。</summary>
        public void StartObserving()
        {
            // メインスレッド外（VMC 受信スレッド等）から流入する可能性があるため ObserveOnMainThread を介する。
            _subscription = _errorChannel.Errors
                .ObserveOnMainThread()
                .Subscribe(OnError, OnStreamError, OnStreamCompleted);
        }

        /// <summary>
        /// 本 spec 内部（Applier 等）から直接エラーを発行する経路（Requirement 2.8 / 4.7）。
        /// メインスレッドからのみ呼び出す。
        /// </summary>
        public void PublishError(string slotId, string errorCode, string detail)
        {
            try
            {
                var trimmed = TrimDetail(detail);
                var errorTopic = CharacterTopics.SlotError(slotId);
                _sink.PublishEvent(errorTopic, new SlotErrorPayload { ErrorCode = errorCode, Detail = trimmed });
                _statusPublisher.Publish(slotId, SlotStateMapper.Error, trimmed);
                LastErrorAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LastErrorMessage = trimmed;
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Error,
                    $"error publish slot={slotId} code={errorCode} detail={trimmed}");
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Error, AdapterLogCategories.Error,
                    $"error publish failed slot={slotId} code={errorCode}", ex);
            }
        }

        private void OnError(SlotError error)
        {
            try
            {
                if (error == null) return;
                var slotId = error.SlotId ?? string.Empty;
                var errorCode = SlotErrorCodeMapper.Map(error.Category, error.Exception);
                var detail = BuildDetail(error);
                var trimmed = TrimDetail(detail);
                _sink.PublishEvent(CharacterTopics.SlotError(slotId),
                    new SlotErrorPayload { ErrorCode = errorCode, Detail = trimmed });
                _statusPublisher.Publish(slotId, SlotStateMapper.Error, trimmed);
                LastErrorAtUnixMs = new DateTimeOffset(error.Timestamp.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();
                LastErrorMessage = trimmed;
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Error,
                    $"channel error slot={slotId} category={error.Category} code={errorCode}", error.Exception);
            }
            catch (Exception ex)
            {
                // 翻訳・送信中の二次例外は最終 catch で握り潰し、Unity Console 警告のみ（Requirement 7.6）。
                try
                {
                    _logger.Log(AdapterLogLevel.Error, AdapterLogCategories.Error,
                        $"OnError threw secondary exception (suppressed)", ex);
                }
                catch
                {
                    UnityEngine.Debug.LogWarning($"[RacMainOutputAdapter/Error] suppressed secondary exception: {ex}");
                }
            }
        }

        private void OnStreamError(Exception ex)
        {
            _logger.Log(AdapterLogLevel.Error, AdapterLogCategories.Error,
                "ISlotErrorChannel.Errors observable produced OnError (unexpected, treating as stream end)", ex);
        }

        private void OnStreamCompleted()
        {
            _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.Lifecycle,
                "ISlotErrorChannel.Errors observable completed.");
        }

        private static string BuildDetail(SlotError error)
        {
            if (error == null) return string.Empty;
            var ex = error.Exception;
            var typeName = ex?.GetType().Name ?? "(none)";
            var message = ex?.Message ?? string.Empty;
            return $"category={error.Category}; type={typeName}; message={message}";
        }

        private string TrimDetail(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return detail;
            var max = _config.MaxErrorDetailLength;
            if (max <= 0 || detail.Length <= max) return detail;
            return detail.Substring(0, max);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
