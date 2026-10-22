using System.Net;
using System.Text;

namespace PayPalWebhookRelay;

public static class RelaySelfCheck
{
    public static async Task<int> RunAsync()
    {
        var upstreamPort = 18766;
        var relayPort = 18765;
        var seen = new Probe();
        using var upstream = new HttpListener();
        upstream.Prefixes.Add($"http://127.0.0.1:{upstreamPort}/");
        upstream.Start();
        _ = Task.Run(() => AcceptUpstreamAsync(upstream, seen));

        await using var relay = WebhookRelay.Start(relayPort, new Uri($"http://127.0.0.1:{upstreamPort}/"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var relayRoot = $"http://127.0.0.1:{relayPort}";
        var failures = 0;
        var noHeaders = new Dictionary<string, string>();

        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, "/admin", [], noHeaders, 404, seen, forwarded: false, "admin-post");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Get, "/admin/login", [], noHeaders, 404, seen, forwarded: false, "admin-get");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Get, WebhookRelay.ExactPath, [], noHeaders, 405, seen, forwarded: false, "webhook-get");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Put, WebhookRelay.ExactPath, [], noHeaders, 405, seen, forwarded: false, "webhook-put");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, "/api/v1/checkout/paypal/capture", [], noHeaders, 404, seen, forwarded: false, "capture-post");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, WebhookRelay.ExactPath + "?evento=1", [], noHeaders, 404, seen, forwarded: false, "webhook-query");
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, WebhookRelay.ExactPath + "/", [], noHeaders, 404, seen, forwarded: false, "webhook-barra");

        var body = new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x01, 0xFF, 0x00 };
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["paypal-transmission-id"] = "probe-id",
            ["paypal-transmission-time"] = "2026-10-20T09:00:00Z",
            ["paypal-transmission-sig"] = "cHJvYmU=",
            ["paypal-cert-url"] = "https://api.sandbox.paypal.com/v1/notifications/certs/CERT-probe",
            ["paypal-auth-algo"] = "SHA256withRSA",
            ["paypal-auth-version"] = "v2",
            ["Cookie"] = "panel=no-reenviar",
            ["Authorization"] = "Bearer no-reenviar"
        };
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, WebhookRelay.ExactPath, body, headers, 204, seen, forwarded: true, "webhook-post");
        if (seen.Hits != 1
            || !seen.Body.AsSpan().SequenceEqual(body)
            || seen.SawCookie
            || seen.SawAuthorization
            || seen.TransmissionId != "probe-id"
            || seen.ContentType != "application/json")
        {
            Console.WriteLine("webhook-post cuerpo-integro=false");
            failures++;
        }
        else
        {
            Console.WriteLine("webhook-post cuerpo-integro=true cookie-reenviada=false autorizacion-reenviada=false");
        }

        var oversized = new byte[WebhookRelay.MaxBodyBytes + 1];
        failures += await ExpectAsync(client, relayRoot, HttpMethod.Post, WebhookRelay.ExactPath, oversized, noHeaders, 413, seen, forwarded: false, "webhook-grande");

        upstream.Close();
        Console.WriteLine(failures == 0 ? "filtro=correcto" : $"filtro=fallos:{failures}");
        return failures == 0 ? 0 : 1;
    }

    private static async Task AcceptUpstreamAsync(HttpListener listener, Probe seen)
    {
        while (listener.IsListening)
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

            seen.Hits++;
            seen.Path = context.Request.Url?.AbsolutePath ?? string.Empty;
            seen.SawCookie = !string.IsNullOrEmpty(context.Request.Headers["Cookie"]);
            seen.SawAuthorization = !string.IsNullOrEmpty(context.Request.Headers["Authorization"]);
            seen.TransmissionId = context.Request.Headers["paypal-transmission-id"];
            seen.ContentType = context.Request.ContentType;
            await using var buffer = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(buffer);
            seen.Body = buffer.ToArray();
            context.Response.StatusCode = 204;
            context.Response.Close();
        }
    }

    private static async Task<int> ExpectAsync(
        HttpClient client,
        string root,
        HttpMethod method,
        string path,
        byte[] body,
        IReadOnlyDictionary<string, string> headers,
        int status,
        Probe seen,
        bool forwarded,
        string name)
    {
        var hitsBefore = seen.Hits;
        using var request = new HttpRequestMessage(method, root + path);
        if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(body);
        }

        foreach (var header in headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content ??= new ByteArrayContent(body);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await client.SendAsync(request);
        var actual = (int)response.StatusCode;
        var didForward = seen.Hits > hitsBefore;
        var ok = actual == status && didForward == forwarded;
        Console.WriteLine($"{name} estado={actual} reenviado={didForward} esperado={status}");
        return ok ? 0 : 1;
    }

    private sealed class Probe
    {
        public int Hits { get; set; }
        public string Path { get; set; } = string.Empty;
        public byte[] Body { get; set; } = [];
        public bool SawCookie { get; set; }
        public bool SawAuthorization { get; set; }
        public string? TransmissionId { get; set; }
        public string? ContentType { get; set; }
    }
}
