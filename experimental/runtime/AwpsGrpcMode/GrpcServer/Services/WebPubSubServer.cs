using AwpsGrpcMode;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WebPubSubServer
{
    public class GrpcTunnel
    {
        // 1. expose a tunnel endpoint for http invoke
        // 2. transform the http invoke into Grpc
        // local tunnel and remote tunnel throught Redis
        /// <summary>
        /// Non-blocking request send
        /// </summary>
        /// <returns></returns>
        public Task SendRequestAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///  Blocking request send
        /// </summary>
        /// <returns></returns>
        public Task<object> InvokeAsync()
        {
            throw new NotImplementedException();
        }

        public Task ScaleupAsync()
        {
            throw new NotImplementedException();
        }
    }
    public class ClientConnectionContext 
    {
        private TaskCompletionSource<ApproveConnectionMessage> _connectTcs = new TaskCompletionSource<ApproveConnectionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ConnectionId { get; }
        public string Hub { get; }
        public HttpContext Context { get; }

        public Task<ApproveConnectionMessage> Connected => _connectTcs.Task;

        public ClientConnectionContext(string hub, HttpContext context)
        {
            ConnectionId = Guid.NewGuid().ToString("N");
            Hub = hub;
            Context = context;
            new CancellationTokenSource(5000).Token.Register(() => _connectTcs.TrySetCanceled());
        }

        public void AllowConnect(ApproveConnectionMessage request)
        {
            _connectTcs.TrySetResult(request);
        }
    }

    public class ConnectionRouter
    {
        private readonly ConcurrentDictionary<string, ConnectionLifetimeManager> _store = new ConcurrentDictionary<string, ConnectionLifetimeManager>();

        public GrpcServerConnectionContext AddEventsListener(string hub, string serverName, string[] events)
        {
            var manager = _store.GetOrAdd(hub, s => new ConnectionLifetimeManager());
            return manager.AddEventListner(hub, serverName, events);
        }

        public bool TryRoute(string hub, HttpContext context, string currentEvent, out (ClientConnectionContext client, GrpcServerConnectionContext routedTo) pair)
        {
            // What if local does not have the connection? never go to a connection without grpc connection?
            // what if grpc for http? grpc to be a tunnel?
            // strategy:
            // 1. Sticky to the one exists
            // 2. Route to local
            // 3. Route to global with evaluated weight
            if (_store.TryGetValue(hub, out var manager))
            {
                var client = manager.AddClient(hub, context);

                // For now we randomly return the first one
                var routedTo = manager.ServerConnections.FirstOrDefault(s => s.Value.CanHandle(currentEvent)).Value;
                if (routedTo != null)
                {
                    pair = (client, routedTo);
                    return true;
                }
            }

            pair = default;
            return false;
        }

        public void AckClientConnect(ApproveConnectionMessage approved)
        {
            if (_store.TryGetValue(approved.Hub, out var manager))
            {
                if (manager.ClientConnections.TryGetValue(approved.ConnectionId, out var client))
                {
                    client.AllowConnect(approved);
                }
            }

            // otherwise go through redis
            // send to connection channel for ack
        }
    }

    public class ConnectionLifetimeManager
    {
        // Need a message queque? How to load balance the message to the grpc client?
        public ConcurrentDictionary<string, GrpcServerConnectionContext> ServerConnections = new ConcurrentDictionary<string, GrpcServerConnectionContext>();

        public ConcurrentDictionary<string, ClientConnectionContext> ClientConnections = new ConcurrentDictionary<string, ClientConnectionContext>();

        public GrpcServerConnectionContext AddEventListner(string hub, string serverName, string[] events)
        {
            var connection = new GrpcServerConnectionContext(hub, serverName, events);
            ServerConnections[connection.ConnectionId] = connection;
            return connection;
        }

        public ClientConnectionContext AddClient(string hub, HttpContext context)
        {
            var connection = new ClientConnectionContext(hub, context);
            ClientConnections[connection.ConnectionId] = connection;
            return connection;
        }
    }

    public class GrpcServerConnectionContext
    {
        public string ConnectionId { get; }
        public string Hub { get; }
        public Channel<Event> QueuedEvents { get; } = Channel.CreateUnbounded<Event>();
        public string ServerName { get; }
        public HashSet<string> Events { get; }

        public GrpcServerConnectionContext(string hub, string serverName, string[] events)
        {
            ConnectionId = Guid.NewGuid().ToString("N");
            Hub = hub;
            ServerName = serverName;
            Events = events.ToHashSet();
        }

        public bool CanHandle(string currentEvent)
        {
            return Events.Contains(currentEvent);
        }
    }

    public class WebPubSubServer : WebPubSub.WebPubSubBase
    {
        private readonly ConnectionRouter _router;
        private readonly ILogger<WebPubSubServer> _logger;
        public WebPubSubServer(ConnectionRouter router, ILogger<WebPubSubServer> logger)
        {
            _router = router;
            _logger = logger;
        }
        public override async Task StartListening(StartListeningRequest request, IServerStreamWriter<Event> responseStream, ServerCallContext context)
        {
            // 1. Act as a server connection and notify all the pods
            // 2. For every pod, it maintains a message queue sending to the connection
            // 3. How can I get the channel ID of the server?
            var server = _router.AddEventsListener(request.Hub, context.Peer, request.InterestedEvents.ToArray());
            while (await server.QueuedEvents.Reader.WaitToReadAsync())
            {
                while (server.QueuedEvents.Reader.TryRead(out var item))
                {
                    await responseStream.WriteAsync(item);
                }
            }
        }

        // Rebalance
        // How to sticky? in the URL creating the channel with targetId
        public override Task<Empty> ApproveConnection(ApproveConnectionMessage request, ServerCallContext context)
        {
            _router.AckClientConnect(request);
            return default;
        }

        public override Task<Empty> SendToGroup(SendToGroupRequest request, ServerCallContext context)
        {
            return base.SendToGroup(request, context);
        }

        public override Task<Empty> SendToAll(SendToAllRequest request, ServerCallContext context)
        {
            Console.WriteLine(request.Payload.Json.StringContent);
            switch (request.Payload.PayloadCase)
            {
                case PayloadDetail.PayloadOneofCase.None:
                    break;
                case PayloadDetail.PayloadOneofCase.Text:
                    Console.Write(request.Payload.Text.DataCase);
                    break;
                case PayloadDetail.PayloadOneofCase.Json:
                    break;
                case PayloadDetail.PayloadOneofCase.Binary:
                    break;
                default:
                    break;
            }

            // how about default
            return default;
        }

    }
}
