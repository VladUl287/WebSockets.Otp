using Microsoft.AspNetCore.Http;
using WebSockets.Otp.Abstractions.Utils;

namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IEndpointAuthorizer
{
    Task<Outcome<Success, string>> AuthorizeAsync(HttpContext context, WsEndpointInfo info);
}
