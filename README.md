[![NuGet Status](https://img.shields.io/nuget/v/WebSockets.Otp.Core.svg?label=project&logo=NuGet&style=flat-square)](https://www.nuget.org/packages/WebSockets.Otp.Core/)
[![NuGet Status](https://img.shields.io/nuget/dt/WebSockets.Otp.Core.svg?label=downloads&logo=NuGet&style=flat-square)](https://www.nuget.org/packages/WebSockets.Otp.Core/)

# WebSockets.Otp

A minimal WebSocket library for ASP.NET Core inspired by REPR principles. Provides a clean endpoint-based API for building real-time applications.

## Full Documentation: [docs](https://vladul287.github.io/)

### Quick Start

## Install Package

```sh
dotnet add package WebSockets.Otp.Core
```

## Configure Services

```cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWsEndpoints();

var app = builder.Build();
app.MapEndpoints(
    "/ws",
    (opt) =>
    {
        opt.OnConnected = async (context) =>
        {
            await context.Manager.AddToGroup("general-chat", context.Connection.Id, default);
        };
        opt.OnDisconnected = async (context) =>
        {
            await context.Manager.RemoveFromGroup("general-chat", context.Connection.Id, default);
        };
    });
```

## Add Request DTO

```cs
public class ChatMessage
{
    public string Username { get; set; }
    public string Text { get; set; }
}
```

## Add Response DTO

```cs
public class ChatMessageSent
{
    public Guid MessageId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
```

## Define your endpoint

```cs
[WsEndpoint("chat/message/send")]
public class SendChatMessageEndpoint : WsEndpoint<ChatMessage, ChatMessageSent>
{
    public override async Task<ChatMessageSent> HandleAsync(ChatMessage message, IEndpointContext context)
    {
        var sent = new ChatMessageSent
        {
            MessageId = Guid.NewGuid(),
            SentAt = DateTimeOffset.UtcNow,
        };

        await context.Send
            .Group("general-chat")
            .SendAsync("chat/message/receive", message, default);

        return sent;
    }
}
```

## Install Client

```sh
npm i websockets.otp.js
```

## Configure Client

```js
const client = useWsClient({
    url: "ws://localhost:5096/ws"
})

await client.connect()

type ChatMessage = { username: string, message: string }
type ChatMessageSent = { messageId: string, sentAt: string }

const off = client.receive<ChatMessage>('chat/message/receive', (msg) => {
    console.log(msg.message) // test message
})

const { messageId, sentAt } = await client.send<ChatMessage, ChatMessageSent>(
    'chat/message/send',
    {
        username: "user1",
        message: "test message",
    })

console.log(messageId, sentAt)
```