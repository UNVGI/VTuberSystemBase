#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public readonly record struct RequestOptions(TimeSpan Timeout);
}
