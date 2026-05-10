#nullable enable
using System;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Diagnostics
{
    /// <summary>
    /// Side-effect-free snapshot of the tab's observable counters for the
    /// diagnostics surface. (task 5.6, 7.3, design.md §Diagnostics.)
    /// </summary>
    public readonly struct TabDiagnosticsSnapshot : IEquatable<TabDiagnosticsSnapshot>
    {
        public TabDiagnosticsSnapshot(
            int totalSlotCount,
            int assignedSlotCount,
            int errorSlotCount,
            int inFlightOperationCount,
            DateTimeOffset? lastSavedAt,
            ConnectionStatusCode connectionStatus,
            string? activePresetId,
            int corruptedPresetBackupCount,
            DateTimeOffset capturedAt)
        {
            TotalSlotCount = totalSlotCount;
            AssignedSlotCount = assignedSlotCount;
            ErrorSlotCount = errorSlotCount;
            InFlightOperationCount = inFlightOperationCount;
            LastSavedAt = lastSavedAt;
            ConnectionStatus = connectionStatus;
            ActivePresetId = activePresetId;
            CorruptedPresetBackupCount = corruptedPresetBackupCount;
            CapturedAt = capturedAt;
        }

        public int TotalSlotCount { get; }
        public int AssignedSlotCount { get; }
        public int ErrorSlotCount { get; }
        public int InFlightOperationCount { get; }
        public DateTimeOffset? LastSavedAt { get; }
        public ConnectionStatusCode ConnectionStatus { get; }
        public string? ActivePresetId { get; }
        public int CorruptedPresetBackupCount { get; }
        public DateTimeOffset CapturedAt { get; }

        public bool Equals(TabDiagnosticsSnapshot other) =>
            TotalSlotCount == other.TotalSlotCount
            && AssignedSlotCount == other.AssignedSlotCount
            && ErrorSlotCount == other.ErrorSlotCount
            && InFlightOperationCount == other.InFlightOperationCount
            && Nullable.Equals(LastSavedAt, other.LastSavedAt)
            && ConnectionStatus == other.ConnectionStatus
            && string.Equals(ActivePresetId, other.ActivePresetId, StringComparison.Ordinal)
            && CorruptedPresetBackupCount == other.CorruptedPresetBackupCount
            && CapturedAt == other.CapturedAt;

        public override bool Equals(object? obj) => obj is TabDiagnosticsSnapshot s && Equals(s);

        public override int GetHashCode() => HashCode.Combine(
            TotalSlotCount, AssignedSlotCount, ErrorSlotCount,
            InFlightOperationCount, ConnectionStatus, ActivePresetId);
    }
}
