using Grpc.Core;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebPubSubServer
{
    public class WebPubSubServer : AwpsGrpcMode.WebPubSub.WebPubSubBase
    {
        private readonly ILogger<WebPubSubServer> _logger;
        public WebPubSubServer(ILogger<WebPubSubServer> logger)
        {
            _logger = logger;
        }
    }
}
