using System;
using System.Threading.Tasks;

using Grpc.Core;
using Grpc.Net.Client;

using AwpsGrpcMode;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrpcClient
{
    class Program
    {
        // TODO: generate token from connection string
        private static string _token = "";
        static async Task Main(string[] args)
        {
            //CreateHostBuilder(args).Build().Run();
            using var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new WebPubSub.WebPubSubClient(channel);
            var request = new StartListeningRequest();
            request.InterestedEvents.Add("connect");
            var events = client.StartListening(request);
            while (await events.ResponseStream.MoveNext())
            {
                var current = events.ResponseStream.Current;
                if (current.ServiceEvent != null)
                {

                }

                if (current.ConnectionEvent != null)
                {
                    switch (current.ConnectionEvent.MessageCase)
                    {
                        case ClientConnectionEvent.MessageOneofCase.ConnectEvent:
                            Console.WriteLine(current.ConnectionEvent.ConnectEvent.Context.ConnectionId);
                            var sendToAll = new SendToAllRequest();
                            sendToAll.Payload.Json.StringContent = "{\"hello\": \"world\"}";
                            await client.SendToAllAsync(sendToAll);
                            var ack = new ApproveConnectionMessage();
                            ack.ConnectionId = current.ConnectionEvent.ConnectEvent.ConnectionId;
                            await client.ApproveConnectionAsync(ack);
                            break;

                        default:
                            throw new NotSupportedException(current.ConnectionEvent.MessageCase.ToString());
                    }
                }
            }
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureServices(
                services =>
                {
                    services.AddGrpcClient<WebPubSub.WebPubSubClient>(o=>
                    {
                        o.Address = new Uri("https://localhost:5001");
                    }).ConfigureChannel(o=>
                    {
                        var credentials = CallCredentials.FromInterceptor((context, metadata) =>
                        {
                            if (!string.IsNullOrEmpty(_token))
                            {
                                metadata.Add("Authorization", $"Bearer {_token}");
                            }
                            return Task.CompletedTask;
                        });

                        o.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);

                    });
                });
    }
}
