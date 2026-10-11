using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Api.Checkout;

/// <summary>
/// Notifications:DispatchEnabled lo apaga. El valor por defecto es false.
/// Solo Development con Delivery=Capture y Mailpit en 127.0.0.1:1025 puede activarlo.
/// </summary>
public sealed class OrderNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderNotificationOptions> options,
    ILogger<OrderNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.DispatchEnabled)
        {
            logger.LogInformation("Envío de avisos desactivado.");
            return;
        }

        var intervalSeconds = Math.Clamp(settings.IntervalSeconds, 5, 300);
        var batch = Math.Clamp(settings.BatchSize, 1, 20);
        logger.LogInformation(
            "Envío de avisos a Mailpit cada {IntervalSeconds} segundos, lotes de {BatchSize}.",
            intervalSeconds,
            batch);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOrderNotificationDispatch>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("El envío de avisos no ha podido completarse. Tipo {ExceptionType}.", exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
