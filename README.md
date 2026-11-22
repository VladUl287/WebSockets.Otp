[![NuGet Status](https://img.shields.io/nuget/v/WebSockets.Otp.Core.svg?label=project&logo=NuGet&style=flat-square)](https://www.nuget.org/packages/WebSockets.Otp.Core/)
[![NuGet Status](https://img.shields.io/nuget/dt/WebSockets.Otp.Core.svg?label=downloads&logo=NuGet&style=flat-square)](https://www.nuget.org/packages/WebSockets.Otp.Core/)

# WebSockets.Otp

A minimal WebSocket library for ASP.NET Core inspired by REPR principles. Provides a clean endpoint-based API for building real-time applications.

## Full Documentation: [docs](https://vladul287.github.io/)

### Quick Start

#### 1. Install

```sh
dotnet add package WebSockets.Otp.Core
```

#### 2. Define your endpoint

```cs
[WsEndpoint("chat/message/send")]
public class ChatEndpoint : WsEndpoint<ChatMessage>
{
    public override async Task HandleAsync(ChatMessage message, IEndpointContext context)
    {
        await context.Send
            .Group("general-chat")
            .SendAsync("chat/message/receive", message, default);
    }
}

public class ChatMessage
{
    public string Username { get; set; }
    public string Message { get; set; }
}
```

#### 3. Configure services

```cs
// Program.cs
builder.Services.AddWsEndpoints();

app.MapEndpoints(
    "/ws",
    (opt) =>
    {
        opt.OnConnected = async (context) =>
        {
            await context.Groups.AddAsync("general-chat", context.Connection.Id);
        };
        opt.OnDisconnected = async (context) =>
        {
            await context.Groups.RemoveAsync("general-chat", context.Connection.Id);
        };
    });
```

#### 4. Install client

```sh
npm i websockets.otp.js
```

#### 5. Configure client

```js
const client = useWsClient({
    url: "ws://localhost:5096/ws"
})

await client.connect()

type Message = { username: string, message: string }

const off = client.receive<Message>('chat/message/receive', (msg) => {
    console.log(msg.message) // test message
})

await client.notify<Message>(
    'chat/message/send',
    {
        username: "user1",
        message: "test message",
    })
```
