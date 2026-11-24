namespace WebSockets.Otp.Abstractions.Endpoints;

public interface IEndpointInvoker
{
    Task Invoke(IEndpointContext context);
}
