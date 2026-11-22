namespace WebSockets.Otp.Abstractions.Endpoints;

public sealed class WsEmptyRequest
{
    public static WsEmptyRequest Instance { get; } = new();
}

public sealed class WsEmptyResponse
{
    public static WsEmptyResponse Instance { get; } = new();
}
