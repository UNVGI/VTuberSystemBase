#nullable enable
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Port that converts a <see cref="CameraSnapshot"/> into a UCAPI Flat Record
    /// (10 byte header + 128 byte record). The default adapter wraps UCAPI4Unity;
    /// tests substitute a Fake that returns a deterministic byte buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations MUST NOT throw on invalid input — they return
    /// <see cref="SerializeResult.Invalid"/> with a structured reason. NaN / Inf
    /// position or rotation, focal length ≤ 0, sensor size ≤ 0, and inverted
    /// near/far clip planes are all sanitized to a failure result.
    /// </para>
    /// <para>
    /// Implementations MUST be safe to call from the LateUpdate frame tick. The
    /// returned <see cref="UcapiFlatRecord"/> is owned by the caller; the
    /// implementation MUST NOT retain a reference to the buffer after return.
    /// </para>
    /// </remarks>
    public interface IUcapiFlatRecordSerializer
    {
        SerializeResult Serialize(in CameraSnapshot snapshot);
    }
}
