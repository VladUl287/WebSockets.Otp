using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.MessagePack.Formatters;

namespace WebSockets.Otp.MessagePack.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessagePackSerializer(this IServiceCollection services)
    {
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(
                CompositeResolver.Create(
                    CustomStringConvertersResolver.Instance,
                    ContractlessStandardResolver.Instance
                )
            );
        return services.AddMessagePackSerializer(options);
    }

    public static IServiceCollection AddMessagePackSerializer(this IServiceCollection services, Action<MessagePackSerializerOptions> configure)
    {
        var options = MessagePackSerializerOptions.Standard
            .WithResolver(
                CompositeResolver.Create(
                    CustomStringConvertersResolver.Instance,
                    ContractlessStandardResolver.Instance
                )
            );
        configure(options);
        return services.AddMessagePackSerializer(options);
    }

    public static IServiceCollection AddMessagePackSerializer(this IServiceCollection services, MessagePackSerializerOptions options)
    {
        return services.AddSingleton<IMessageSerializer>(new MessagePackMessageSerializer(options));
    }
}
