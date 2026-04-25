#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface IAuthenticationHandler
    {
        Task<IpcResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken cancellationToken);
    }

    public readonly record struct AuthenticationContext(
        string RemoteEndpoint,
        string? Origin,
        string? AuthorizationHeader);
}
