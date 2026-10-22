using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Sonda aislada. No forma parte de la API, no lee la configuración del checkout
// y no crea pedidos, capturas ni webhooks. Solo Client ID y Secret del proceso.
const string SandboxApiRoot = "https://api-m.sandbox.paypal.com";
const string TokenPath = "/v1/oauth2/token";

var clientId = Environment.GetEnvironmentVariable("PayPal__ClientId");
var clientSecret = Environment.GetEnvironmentVariable("PayPal__ClientSecret");
if (!IsCredential(clientId) || !IsCredential(clientSecret))
{
    Console.Error.WriteLine("Faltan PayPal__ClientId o PayPal__ClientSecret en el entorno de este proceso, o no cumplen el formato (8 a 127 caracteres, sin espacios).");
    Console.Error.WriteLine("Defínelas en esta misma ventana. No las pegues en el chat ni en el repositorio. Esta comprobación no usa WebhookId ni activa el checkout.");
    return 2;
}

if (!Uri.TryCreate(SandboxApiRoot, UriKind.Absolute, out var root)
    || !string.Equals(root.GetLeftPart(UriPartial.Authority), SandboxApiRoot, StringComparison.OrdinalIgnoreCase)
    || IsLiveHost(root.Host))
{
    Console.Error.WriteLine("El host de la comprobación no es el Sandbox.");
    return 3;
}

using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
using var http = new HttpClient(handler)
{
    BaseAddress = root,
    Timeout = TimeSpan.FromSeconds(20)
};

using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath);
var raw = Encoding.UTF8.GetBytes(clientId + ":" + clientSecret);
request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials"
});

HttpResponseMessage response;
try
{
    response = await http.SendAsync(request);
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    Console.Error.WriteLine($"Error de transporte ({exception.GetType().Name}).");
    return 1;
}

using (response)
{
    var status = (int)response.StatusCode;
    if (status is >= 300 and <= 399)
    {
        Console.WriteLine($"HTTP {status}. Redirección rechazada.");
        return 1;
    }

    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"HTTP {status}. Error.");
        return 1;
    }

    try
    {
        using var document = JsonDocument.Parse(body);
        var payload = document.RootElement;
        var hasToken = payload.TryGetProperty("access_token", out var tokenProperty)
            && tokenProperty.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(tokenProperty.GetString());
        var tokenType = payload.TryGetProperty("token_type", out var typeProperty)
            && typeProperty.ValueKind == JsonValueKind.String
            ? typeProperty.GetString()
            : null;
        var expires = payload.TryGetProperty("expires_in", out var expiresProperty) && expiresProperty.TryGetInt32(out var seconds)
            ? seconds
            : 0;
        if (!hasToken || !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || expires <= 0)
        {
            Console.WriteLine($"HTTP {status}. Respuesta sin tipo de token o caducidad.");
            return 1;
        }

        Console.WriteLine($"HTTP {status}. Tipo de token: Bearer. Validez: {expires} segundos.");
        return 0;
    }
    catch (JsonException)
    {
        Console.WriteLine($"HTTP {status}. Respuesta no interpretable.");
        return 1;
    }
}

static bool IsCredential(string? value)
{
    return value is not null && value.Length is >= 8 and <= 127 && !value.Any(char.IsWhiteSpace);
}

static bool IsLiveHost(string host)
{
    return host.Equals("api-m.paypal.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("api.paypal.com", StringComparison.OrdinalIgnoreCase);
}
