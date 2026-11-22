using WebSockets.Otp.Abstractions.Contracts;
using WebSockets.Otp.Abstractions.Endpoints;
using WebSockets.Otp.Abstractions.Options;
using WebSockets.Otp.Abstractions.Serializers;
using WebSockets.Otp.Abstractions.Transport;

namespace WebSockets.Otp.Core.Processors;

public sealed class ParallelMessageProcessor(IMessageDispatcher dispatcher, IMessageEnumerator enumerator) : IMessageProcessor
{
    public async Task Process(
        IGlobalContext context, IMessageSerializer serializer, WsOptions options, CancellationToken token)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            TaskScheduler = options.TaskScheduler,
            CancellationToken = token
        };

        var messages = enumerator.EnumerateAsync(context.Socket, options, token);

        await Parallel.ForEachAsync(messages, parallelOptions, async (buffer, token) =>
        {
            try
            {
                await dispatcher.DispatchMessage(context, serializer, buffer, token);
            }
            finally
            {
                enumerator.ReturnBuffer(buffer);
            }
        });
    }
}
