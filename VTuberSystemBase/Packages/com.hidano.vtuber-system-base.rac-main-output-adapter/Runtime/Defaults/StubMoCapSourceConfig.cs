using RealtimeAvatarController.Core;
using UnityEngine;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// 本 spec が提供する no-op MoCap Source Config（Requirement 8.4 / RA-4）。
    /// </summary>
    /// <remarks>
    /// RAC v0.2.0 は Stub MoCap Source を同梱していないため、本 spec が <c>MoCapSourceConfigBase</c> を継承する
    /// Stub Config 型を提供する。<see cref="StubMoCapSourceConfigFactory"/> が <see cref="ScriptableObject.CreateInstance{T}"/>
    /// で動的生成し、Slot 単位で <see cref="MoCapSourceDescriptor"/> に詰める。
    /// 利用者プロジェクトは <c>IMoCapSourceConfigFactory</c> を差し替えて VMC 等の具体ソースに置き換える。
    /// </remarks>
    public sealed class StubMoCapSourceConfig : MoCapSourceConfigBase
    {
    }
}
