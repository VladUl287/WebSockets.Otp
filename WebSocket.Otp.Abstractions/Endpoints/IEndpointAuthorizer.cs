using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IEndpointAuthorizer
{
    Task<Result<Success, Error<string>>> AuthorizeAsync(IServiceProvider services, HttpContext context, WsEndpointInfo info);
}
