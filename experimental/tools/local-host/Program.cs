
using Microsoft.Azure.Relay;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Net;


var app = new CommandLineApplication();
app.Name = "local-host";
app.Description = "The local tunnel tool to help localhost accessible by Azure Web PubSub service";
app.HelpOption("-h|--help");
// local-host login
// local-host list
// local-host bind -g <resourcegroup> -n <instancename>
// local-host unbind -g <resourcegroup> -n <instancename>
// local-host reset
// local-host show (show bind resources)
// local-host -p 8080
var portOptions = app.Option("-p|--port", "Specify the port of localhost server", CommandOptionType.SingleValue);
app.Command("bind", command =>
{
    command.Description = "Bind to the Web PubSub service";
    command.HelpOption("-h|--help");
    var resourceGroupOption = command.Option("-g|--reourceGroup", "Specify the resourceGroup of the instance.", CommandOptionType.SingleValue);
    var resourceNameOption = command.Option("-n|--name", "Specify the name of the instance.", CommandOptionType.SingleValue);
    command.OnExecute(() =>
    {
        return 0;
    });
});

app.OnExecute(async () =>
{
    if (int.TryParse(portOptions.Value(), out var port) )
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient();
                services.AddSingleton<TunnelService>();
                services.Configure<TunnelServiceOptions>(o =>
                {
                    o.LocalPort = port;
                });
            }).ConfigureLogging(o => o.AddConsole())
            .Build();

        await host.Services.GetRequiredService<TunnelService>().StartAsync();
    }
    else
    {
        app.ShowHelp();
    }
    return 0;
});

try
{
    app.Execute(args);
}
catch (Exception e)
{
    Console.WriteLine($"Error occured: {e.Message}.");
}

internal class TunnelServiceOptions
{
    public string RelayServiceConnectionString {get; set;}
    public int LocalPort { get; set; }
    public string LocalScheme { get; set; }
}

static class HttpExtensions
{
    public static string GetDisplayUrl(this RelayedHttpListenerContext context)
    {
        var request = context.Request;
        var uri = request.Url;
        var method = request.HttpMethod;
        var body = request.Headers[HttpResponseHeader.ContentLength];
        return $"{method} {uri} {body}";
    }

    public static string GetDisplayUrl(this HttpRequestMessage request)
    {
        var uri = request.RequestUri?.OriginalString ?? string.Empty;
        var method = request.Method;
        var body = request.Content?.Headers.ContentLength;
        return $"{method} {uri} {body}";
    }

}

internal class TunnelService
{
    private readonly TunnelServiceOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TunnelService> _logger;

    public TunnelService(IOptions<TunnelServiceOptions> options, IHttpClientFactory httpClientFactory, ILogger<TunnelService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static Dictionary<string, string> ParsedConnectionString(string conn)
    {
        return conn.Split(";").Select(s => s.Split("=")).Where(i => i.Length == 2).ToDictionary(j => j[0], j => j[1], StringComparer.OrdinalIgnoreCase);
    }

    private static (string Endpoint, string EntityPath) Parse(string conn)
    {
        if (conn == null)
        {
            throw new ArgumentException("RelayServiceConnectionString is not set");
        }
        var parsed = ParsedConnectionString(conn);
        if (!parsed.TryGetValue("entityPath", out var entityPath))
        {
            throw new ArgumentException("EntityPath is expected to be found in connectionString");
        }

        if (!parsed.TryGetValue("endpoint", out var endpoint))
        {
            throw new ArgumentException("endpoint is expected to be found in connectionString");
        }

        return (endpoint, entityPath);
    }

    public async Task StartAsync()
    {
        var (endpoint, entityPath) = Parse(_options.RelayServiceConnectionString);
        var uriBuilder = new UriBuilder(endpoint);
        uriBuilder.Scheme = "https";
        var publicUrl = $"{uriBuilder.Uri}{entityPath}";
        _logger.LogInformation($"Serving requests from {publicUrl}");

        var cts = new CancellationTokenSource();
        var listener = new HybridConnectionListener(_options.RelayServiceConnectionString);

        // Subscribe to the status events.
        listener.Connecting += (o, e) => { Console.WriteLine("Connecting"); };
        listener.Offline += (o, e) => { Console.WriteLine("Offline"); };
        listener.Online += (o, e) => { Console.WriteLine("Online"); };

        // Provide an HTTP request handler
        listener.RequestHandler = async (context) =>
        {
            using var httpClient = _httpClientFactory.CreateClient();
            _logger.LogInformation($"Received request from {context.GetDisplayUrl()}");
            //https://zityang-myrelaycap7qxjzpzfkq.servicebus.windows.net/zityang-worksta
            var targetUri = new UriBuilder("http", "localhost", _options.LocalPort).Uri;
            var proxiedRequest = CreateProxyHttpRequest(context, targetUri, "/" + entityPath);
            _logger.LogInformation($"Proxied request to {proxiedRequest.GetDisplayUrl()}");
            try
            {
                using var responseMessage = await httpClient.SendAsync(proxiedRequest);
                // Do something with context.Request.Url, HttpMethod, Headers, InputStream...
                context.Response.StatusCode = responseMessage.StatusCode;
                _logger.LogInformation($"Received response status code: {responseMessage.StatusCode}");

                foreach (var (key, header) in responseMessage.Headers)
                {
                    //if (string.Equals("Cache-Control", key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals("Connection", key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals("Keep-Alive", key, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var val in header)
                    {
                        context.Response.Headers.Add(key, val);
                    }
                }

                if (responseMessage.Content != null)
                {
                    foreach (var (key, header) in responseMessage.Content.Headers)
                    {
                        foreach (var val in header)
                        {
                            context.Response.Headers.Add(key, val);
                        }
                    }

                    await responseMessage.Content.CopyToAsync(context.Response.OutputStream);
                }

                // The context MUST be closed here
                context.Response.Close();
            }
            catch (Exception e)
            {
                _logger.LogError($"Error forwarding request {proxiedRequest.GetDisplayUrl()}: {e.Message}");
                context.Response.StatusCode = HttpStatusCode.InternalServerError;
                context.Response.StatusDescription = e.Message;
                context.Response.Close();
            }
        };

        // Opening the listener establishes the control channel to
        // the Azure Relay service. The control channel is continuously 
        // maintained, and is reestablished when connectivity is disrupted.
        await listener.OpenAsync();
        Console.WriteLine("Server listening");

        // Start a new thread that will continuously read the console.
        await Console.In.ReadLineAsync();

        // Close the listener after you exit the processing loop.
        await listener.CloseAsync();
    }


    public static HttpRequestMessage CreateProxyHttpRequest(RelayedHttpListenerContext context, Uri uri, string pathBase)
    {
        var requestMessage = new HttpRequestMessage();
        var requestMethod = context.Request.HttpMethod;
        if (context.Request.HasEntityBody)
        {
            var streamContent = new StreamContent(context.Request.InputStream);
            requestMessage.Content = streamContent;
        }

        // Copy the request headers
        foreach (var header in context.Request.Headers.AllKeys)
        {
            if (!requestMessage.Headers.TryAddWithoutValidation(header, context.Request.Headers[header]) && requestMessage.Content != null)
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header, context.Request.Headers[header]);
            }
        }

        requestMessage.Headers.Host = uri.Host;
        requestMessage.Method = new HttpMethod(requestMethod);
        var uriBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.Port, context.Request.Url.LocalPath.Substring(pathBase.Length), context.Request.Url.Query);
        requestMessage.RequestUri = uriBuilder.Uri;
        
        return requestMessage;
    }
}