using WebSockets.Otp.Abstractions.Endpoints;

namespace WebSockets.Otp.Core.Extensions;

internal static class EndpointsExtensions
{
    internal static bool IsWsEndpoint(this Type type) =>
        type is { IsAbstract: false, IsInterface: false, IsClass: true, IsPublic: true } &&
        typeof(IWsEndpoint).IsAssignableFrom(type);

    internal static Type GetBaseEndpointType(this Type type)
    {
        var current = type;

        while (true)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(WsEndpoint<,>))
                return current;

            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(WsEndpoint<>))
                return current;

            if (current == typeof(WsEndpoint))
                return current;

            current = current.BaseType
                ?? throw new NotSupportedException($"Type {current} does not inherit from WsEndpoint");
        }
    }
}
