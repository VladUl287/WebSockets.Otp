using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using WebSockets.Otp.Abstractions.Endpoints;

namespace WebSockets.Otp.Abstractions.Options;

public sealed class WsConfig
{
    public IList<IAuthorizeData> AuthorizationData { get; set; } = [];

    public WebSocketOptions WebSocketOptions { get; set; } = new();

    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount * 10;
    public TaskScheduler? TaskScheduler { get; set; }

    public int DefaultBufferSize { get; set; } = 4 * 1024; // 4KB
    public int MaxBufferSize { get; set; } = 4 * 1024 * 1024; // 4MB
    public bool ShrinkBuffer { get; set; } = true;

    public Func<IGlobalContext, Task>? OnConnected { get; set; }
    public Func<IGlobalContext, Task>? OnDisconnected { get; set; }
}

public sealed class WsOptions(WsConfig options)
{
    public IList<IAuthorizeData> AuthorizationData { get; init; } = options.AuthorizationData ?? [];

    public WebSocketOptions WebSocketOptions { get; init; } = options.WebSocketOptions ?? new();

    public int MaxDegreeOfParallelism { get; init; } = options.MaxDegreeOfParallelism;
    public TaskScheduler? TaskScheduler { get; init; } = options.TaskScheduler;

    public int DefaultBufferSize { get; init; } = options.DefaultBufferSize;
    public int MaxBufferSize { get; init; } = options.MaxBufferSize;
    public bool ShrinkBuffer { get; init; } = options.ShrinkBuffer;

    public Func<IGlobalContext, Task>? OnConnected { get; init; } = options.OnConnected;
    public Func<IGlobalContext, Task>? OnDisconnected { get; init; } = options.OnDisconnected;
}
