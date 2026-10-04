using System.Net;

namespace DominioDeLaSierra.IntegrationTests;

internal sealed class CookieHandler : DelegatingHandler
{
    private readonly CookieContainer cookies = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("La petición no tiene URI.");
        var header = cookies.GetCookieHeader(uri);
        if (header.Length > 0)
        {
            request.Headers.Remove("Cookie");
            request.Headers.TryAddWithoutValidation("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                cookies.SetCookies(uri, setCookie);
            }
        }

        return response;
    }
}
