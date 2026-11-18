namespace WebSockets.Otp.Abstractions.Options;

public sealed record HandshakeOptions(string Protocol);

public abstract record HandshakeError
{
    public sealed record NoMessage : HandshakeError;
    public sealed record UnsupportedProtocol(string Protocol) : HandshakeError;
    public sealed record DeserializeFailed : HandshakeError;
    public sealed record SendFailed(Exception Cause) : HandshakeError;
}