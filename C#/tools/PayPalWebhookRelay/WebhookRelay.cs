using System.Net;
using System.Text;

namespace PayPalWebhookRelay;

/// <summary>
/// Reenvía solo el webhook de PayPal. La URL pública no autentica: la API verifica la firma.
/// </summary>
public sealed class WebhookRelay : IAsyncDisposable
{
    public const string ExactPath = "/api/v1/checkout/paypal/webhook";
    public const string RedsysPath = "/api/v1/checkout/redsys/notification";
    public const int MaxBodyBytes = 256 * 1024;

    private static readonly string[] ForwardedHeaders =
    [
        "Content-Type",
        "paypal-auth-algo",
        "paypal-auth-version",
        "paypal-cert-url",
        "paypal-transmission-id",
        "paypal-transmission-sig",
        "paypal-transmission-time"
    ];

    private readonly HttpListener listener = new();
    private readonly HttpClient upstream;
    private readonly Uri upstreamRoot;
    private readonly CancellationTokenSource stopping = new();

    private WebhookRelay(int port, Uri apiRoot)
    {
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        upstreamRoot = apiRoot;
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        upstream = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public static WebhookRelay Start(int port, Uri upstreamRoot)
    {
        if (!IsLoopbackRoot(upstreamRoot))
        {
            throw new InvalidOperationException("El reenvío solo puede apuntar a una API en localhost.");
        }

        var relay = new WebhookRelay(port, upstreamRoot);
        relay.listener.Start();
        _ = Task.Run(relay.ListenAsync);
        return relay;
    }

    public static bool IsLoopbackRoot(Uri uri)
    {
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        listener.Close();
        upstream.Dispose();
        await Task.CompletedTask;
    }

    private async Task ListenAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var url = context.Request.Url;
            var path = url?.AbsolutePath ?? string.Empty;
            var query = url?.Query ?? string.Empty;
            var paypal = string.Equals(path, ExactPath, StringComparison.Ordinal);
            var redsys = string.Equals(path, RedsysPath, StringComparison.Ordinal);
            if ((!paypal && !redsys) || query.Length > 0)
            {
                await WriteAsync(context.Response, 404, "No encontrado.");
                Console.Error.WriteLine("Petición rechazada.");
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
            {
                await WriteAsync(context.Response, 405, "Método no admitido.");
                Console.Error.WriteLine("Petición rechazada.");
                return;
            }

            var body = await ReadBodyAsync(context.Request);
            if (body is null)
            {
                await WriteAsync(context.Response, 413, "Cuerpo demasiado grande.");
                Console.Error.WriteLine("Petición rechazada.");
                return;
            }

            using var forwarded = new HttpRequestMessage(HttpMethod.Post, new Uri(upstreamRoot, path));
            forwarded.Content = new ByteArrayContent(body);
            foreach (var name in ForwardedHeaders)
            {
                if (!paypal && !string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = context.Request.Headers[name];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (value.Length > 2048 || value.Contains('\r') || value.Contains('\n'))
                {
                    await WriteAsync(context.Response, 400, "Cabecera no admitida.");
                    Console.Error.WriteLine("Petición rechazada.");
                    return;
                }

                if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    forwarded.Content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    forwarded.Headers.TryAddWithoutValidation(name, value);
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await upstream.SendAsync(forwarded, stopping.Token);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"Webhook no reenviado. Tipo {exception.GetType().Name}.");
                await WriteAsync(context.Response, 502, "La API local no responde.");
                return;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsByteArrayAsync(stopping.Token);
                if (payload.Length > 16 * 1024)
                {
                    await WriteAsync(context.Response, 502, "La API local no responde.");
                    Console.Error.WriteLine("Webhook no reenviado.");
                    return;
                }

                var status = (int)response.StatusCode;
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
                Console.Error.WriteLine($"Webhook reenviado. HTTP {status}.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Petición rechazada. Tipo {exception.GetType().Name}.");
            try
            {
                context.Response.Abort();
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBodyBytes)
        {
            return null;
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length <= MaxBodyBytes)
        {
            var read = await request.InputStream.ReadAsync(chunk);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length > MaxBodyBytes ? null : buffer.ToArray();
    }

    private static async Task WriteAsync(HttpListenerResponse response, int status, string message)
    {
        var payload = Encoding.UTF8.GetBytes($"{{\"error\":\"{message}\"}}");
        response.StatusCode = status;
        response.ContentType = "application/json";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
        response.Close();
    }
}
