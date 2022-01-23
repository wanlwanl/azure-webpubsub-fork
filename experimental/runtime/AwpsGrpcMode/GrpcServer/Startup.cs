using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebPubSubServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
            services.AddSingleton<ConnectionRouter>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseWebSockets();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<WebPubSubServer>();

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                });

                async Task<(byte[] content, WebSocketMessageType formt)> ReadFrameAsync(WebSocket webSocket)
                {
                    using (var stream = new MemoryStream())
                    {
                        var smallBuffer = new byte[1];
                        var received = await webSocket.ReceiveAsync(smallBuffer, CancellationToken.None);
                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            return default;
                        }
                        stream.Write(smallBuffer, 0, received.Count);

                        while (!received.EndOfMessage)
                        {
                            // needs to cancel the while
                            var readBuffer = new byte[2048];
                            received = await webSocket.ReceiveAsync(readBuffer, CancellationToken.None);
                            stream.Write(readBuffer, 0, received.Count);
                        }
                        return (stream.ToArray(), received.MessageType);
                    }
                }

                endpoints.MapGet("/client/hubs/{hub}", async context =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var router = context.RequestServices.GetRequiredService<ConnectionRouter>();
                        var hub = context.Request.RouteValues["hub"] as string;

                        if (router.TryRoute(hub, context, "connect", out var routeInfo))
                        {
                            var routedTo = routeInfo.routedTo;
                            var client = routeInfo.client;
                            var connectEvent = new AwpsGrpcMode.Event();
                            connectEvent.ConnectionEvent.ConnectEvent.ConnectionId = client.ConnectionId;
                            foreach (var header in context.Request.Headers)
                            {
                                connectEvent.ConnectionEvent.ConnectEvent.Context.Headers.Add(header.Key, header.Value.ToString());
                            }

                            await routedTo.QueuedEvents.Writer.WriteAsync(connectEvent);
                            var response = await client.Connected;
                            if (response.ResponseCase == AwpsGrpcMode.ApproveConnectionMessage.ResponseOneofCase.Success)
                            {
                                using (var webSocket = await context.WebSockets.AcceptWebSocketAsync(response.Success.SubProtocol))
                                {
                                    byte[] frame;
                                    WebSocketMessageType format;
                                    do
                                    {
                                        (frame, format) = await ReadFrameAsync(webSocket);
                                        var userEvent = new AwpsGrpcMode.Event();
                                        userEvent.ConnectionEvent.UserEvent.ConnectionId = client.ConnectionId;
                                        userEvent.ConnectionEvent.UserEvent.Context = connectEvent.ConnectionEvent.ConnectEvent.Context;
                                        if (format == WebSocketMessageType.Text)
                                        {
                                            userEvent.ConnectionEvent.UserEvent.Payload.Text.StringContent = Encoding.UTF8.GetString(frame);
                                        }
                                        else
                                        {
                                            userEvent.ConnectionEvent.UserEvent.Payload.Binary.Data = Google.Protobuf.ByteString.CopyFrom(frame);
                                        }
                                        await routedTo.QueuedEvents.Writer.WriteAsync(userEvent);
                                    }
                                    while (frame != null);
                                }
                            }
                            else
                            {
                                context.Response.StatusCode = 400;
                            }
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });
            });
        }
    }
}
