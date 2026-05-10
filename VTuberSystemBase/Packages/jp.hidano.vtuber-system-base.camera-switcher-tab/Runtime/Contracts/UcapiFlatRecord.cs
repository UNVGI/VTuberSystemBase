#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Immutable carrier for the UCAPI Flat Record (10 byte header + 128 byte
    /// record) that <see cref="IUcapiFlatRecordSerializer"/> emits. The exact
    /// CRC and header bytes are populated by the adapter (UCAPI4Unity DLL); this
    /// struct only owns the resulting byte buffer and exposes its size for
    /// validation by callers.
    /// </summary>
    /// <remarks>
    /// The buffer is owned by the struct (no aliasing of the producer's array)
    /// to make the value safe to enqueue across threads (uOSC sends from a
    /// worker thread). Use <see cref="Empty"/> for the unset state.
    /// </remarks>
    public readonly struct UcapiFlatRecord : IEquatable<UcapiFlatRecord>
    {
        /// <summary>Total wire size: 10 byte header + 128 byte record = 138 bytes.</summary>
        public const int ExpectedSize = 138;

        private readonly byte[]? _buffer;

        private UcapiFlatRecord(byte[]? buffer)
        {
            _buffer = buffer;
        }

        /// <summary>Creates a record from <paramref name="buffer"/>. The array is taken by reference (no copy).</summary>
        public static UcapiFlatRecord FromBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return new UcapiFlatRecord(buffer);
        }

        /// <summary>The unset / "no record produced" instance. <see cref="HasValue"/> returns false.</summary>
        public static UcapiFlatRecord Empty => default;

        /// <summary>True if a buffer is present.</summary>
        public bool HasValue => _buffer != null;

        /// <summary>Length of the underlying buffer, or 0 when <see cref="HasValue"/> is false.</summary>
        public int Length => _buffer?.Length ?? 0;

        /// <summary>
        /// Returns the underlying buffer for the OSC adapter to send. The caller
        /// MUST treat the returned array as read-only (no in-place mutation).
        /// </summary>
        public byte[] AsBytes()
        {
            return _buffer ?? Array.Empty<byte>();
        }

        public bool Equals(UcapiFlatRecord other)
        {
            return ReferenceEquals(_buffer, other._buffer);
        }

        public override bool Equals(object? obj) => obj is UcapiFlatRecord other && Equals(other);

        public override int GetHashCode() => _buffer?.GetHashCode() ?? 0;

        public static bool operator ==(UcapiFlatRecord a, UcapiFlatRecord b) => a.Equals(b);

        public static bool operator !=(UcapiFlatRecord a, UcapiFlatRecord b) => !a.Equals(b);
    }
}
