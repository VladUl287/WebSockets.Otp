using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Redis.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddRedisManager(this IServiceCollection services)
    {
        services.AddSingleton<IWsConnectionManager, RedisConnectionManager>();
        return services;
    }
}
