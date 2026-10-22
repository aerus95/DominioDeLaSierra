using System.Net;
using PayPalWebhookRelay;

if (args.Contains("--self-check"))
{
    return await RelaySelfCheck.RunAsync();
}

var port = 5088;
var upstream = new Uri("http://127.0.0.1:5141/");
if (!WebhookRelay.IsLoopbackRoot(upstream))
{
    Console.Error.WriteLine("El destino no es localhost. El receptor no arranca.");
    return 2;
}

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

try
{
    await using var relay = WebhookRelay.Start(port, upstream);
    Console.Error.WriteLine($"Receptor en http://127.0.0.1:{port}. Solo admite POST {WebhookRelay.ExactPath}.");
    await stopped.Task;
    return 0;
}
catch (HttpListenerException)
{
    Console.Error.WriteLine("No se ha podido escuchar en 127.0.0.1:5088.");
    return 1;
}
