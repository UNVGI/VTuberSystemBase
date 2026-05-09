#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IUcapiFlatRecordSerializer"/>. By default returns
    /// a 138 byte buffer with the frame counter encoded in the first 4 bytes so
    /// tests can observe per-frame distinct outputs without depending on UCAPI.
    /// Set <see cref="ForceFailure"/> to make the next <see cref="Serialize"/> call
    /// return the configured failure.
    /// </summary>
    public sealed class FakeFlatRecordSerializer : IUcapiFlatRecordSerializer
    {
        public List<CameraSnapshot> Calls { get; } = new List<CameraSnapshot>();
        public SerializeFailureReason? ForceFailure { get; set; }

        public SerializeResult Serialize(in CameraSnapshot snapshot)
        {
            Calls.Add(snapshot);
            if (ForceFailure is { } reason)
            {
                return SerializeResult.Invalid(reason);
            }
            var buffer = new byte[UcapiFlatRecord.ExpectedSize];
            buffer[0] = (byte)(snapshot.FrameCounter & 0xFF);
            buffer[1] = (byte)((snapshot.FrameCounter >> 8) & 0xFF);
            buffer[2] = (byte)((snapshot.FrameCounter >> 16) & 0xFF);
            buffer[3] = (byte)((snapshot.FrameCounter >> 24) & 0xFF);
            return SerializeResult.Ok(UcapiFlatRecord.FromBytes(buffer));
        }
    }
}
