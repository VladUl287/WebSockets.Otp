using WebSockets.Otp.Abstractions.Connection;

namespace WebSockets.Otp.Abstractions.Utils.Managers;

public record struct ResponseManger(
    IWsConnection Caller, CorrelationId CorrelationId, IWsConnectionManager Manager)
{
    public bool IsResponseSettled { get; private set; }

    public async ValueTask SendResponseAsync<TData>(TData value, CancellationToken ct = default)
        where TData : notnull
    {
        if (IsResponseSettled)
            throw new InvalidOperationException("Response already settled");

        if (CorrelationId.IsEmpty)
            throw new InvalidOperationException("Client not expecting response. CorrelationId not presented.");

        var envelope = new ResponseMessage(CorrelationId, value);
        await Manager.Send(WsTarget.ToConnection(Caller.Id), envelope, ct);
        IsResponseSettled = true;
    }

    public ValueTask SendErrorAsync(string message, CancellationToken ct = default) =>
        SendErrorAsync(message, WsErrorCode.Internal, ct);

    public async ValueTask SendErrorAsync(string message, WsErrorCode code, CancellationToken ct = default)
    {
        if (IsResponseSettled)
            throw new InvalidOperationException("Response already settled");

        if (CorrelationId.IsEmpty)
            throw new InvalidOperationException("Client not expecting response. CorrelationId not presented.");

        var envelope = new ErrorMessage(CorrelationId, code, message);
        await Manager.Send(WsTarget.ToConnection(Caller.Id), envelope, ct);
        IsResponseSettled = true;
    }
}
