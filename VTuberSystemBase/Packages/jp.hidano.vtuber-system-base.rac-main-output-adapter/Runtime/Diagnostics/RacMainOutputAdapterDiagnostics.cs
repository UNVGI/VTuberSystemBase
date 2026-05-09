using System;
using System.Threading;
using RealtimeAvatarController.Core;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using VTuberSystemBase.RacMainOutputAdapter.Senders;

namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// <see cref="IRacMainOutputAdapterDiagnostics"/> の内部実装（Requirement 10.7）。
    /// </summary>
    internal sealed class RacMainOutputAdapterDiagnostics : IRacMainOutputAdapterDiagnostics
    {
        private readonly Func<int> _registeredHandlerCountProvider;
        private readonly Func<SlotManager> _slotManagerProvider;
        private readonly Func<IAvatarKeyResolver> _keyResolverProvider;
        private readonly Func<SlotErrorTranslator> _errorTranslatorProvider;

        private string _phaseName = "Idle";
        private int _errorSlotCount;

        /// <summary>本 Diagnostics を生成する。各依存は遅延評価のための Func で受け取る（Bootstrapper の生成順序に整合）。</summary>
        public RacMainOutputAdapterDiagnostics(
            Func<int> registeredHandlerCountProvider,
            Func<SlotManager> slotManagerProvider,
            Func<IAvatarKeyResolver> keyResolverProvider,
            Func<SlotErrorTranslator> errorTranslatorProvider)
        {
            _registeredHandlerCountProvider = registeredHandlerCountProvider ?? (() => 0);
            _slotManagerProvider = slotManagerProvider ?? (() => null);
            _keyResolverProvider = keyResolverProvider ?? (() => null);
            _errorTranslatorProvider = errorTranslatorProvider ?? (() => null);
        }

        /// <summary>現在のフェーズ名（"Idle" / "Initializing" / "Ready" / "ShuttingDown" / "Shutdown"）。</summary>
        public string PhaseName
        {
            get => Volatile.Read(ref _phaseName);
            set => Volatile.Write(ref _phaseName, value ?? "Idle");
        }

        /// <summary>エラー Slot のカウントをセットする（外部からの集計反映用）。</summary>
        public void SetErrorSlotCount(int count) => Volatile.Write(ref _errorSlotCount, count);

        /// <inheritdoc/>
        public RacAdapterDiagnosticsSnapshot Capture()
        {
            int handlerCount = _registeredHandlerCountProvider();
            int activeSlotCount = 0;
            try
            {
                var sm = _slotManagerProvider();
                if (sm != null)
                {
                    var slots = sm.GetSlots();
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (slots[i].State == SlotState.Active) activeSlotCount++;
                    }
                }
            }
            catch
            {
                // SlotManager Dispose 直後に呼ばれた場合などはカウント 0 で続行
            }

            int avatarCatalogSize = 0;
            try
            {
                var resolver = _keyResolverProvider();
                if (resolver != null) avatarCatalogSize = resolver.AvatarKeys?.Count ?? 0;
            }
            catch
            {
            }

            long lastErrorAt = 0;
            string lastErrorMsg = string.Empty;
            try
            {
                var et = _errorTranslatorProvider();
                if (et != null)
                {
                    lastErrorAt = et.LastErrorAtUnixMs;
                    lastErrorMsg = et.LastErrorMessage ?? string.Empty;
                }
            }
            catch
            {
            }

            return new RacAdapterDiagnosticsSnapshot(
                RegisteredHandlerCount: handlerCount,
                ActiveSlotCount: activeSlotCount,
                ErrorSlotCount: Volatile.Read(ref _errorSlotCount),
                LastErrorAtUnixMs: lastErrorAt,
                LastErrorMessage: lastErrorMsg,
                AvatarCatalogSize: avatarCatalogSize,
                PhaseName: PhaseName);
        }
    }
}
