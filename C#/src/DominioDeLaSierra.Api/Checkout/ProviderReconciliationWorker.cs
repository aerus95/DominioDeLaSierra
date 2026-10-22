using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Api.Checkout;

/// <summary>
/// Concilia Redsys y PayPal sin liberar stock por antigüedad.
/// Reservations:ProviderReconciliationEnabled lo apaga. El valor por defecto es false.
/// </summary>
public sealed class ProviderReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSweepOptions> options,
    ILogger<ProviderReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ProviderReconciliationEnabled)
        {
            logger.LogInformation("Conciliación de Redsys y PayPal desactivada.");
            return;
        }

        var intervalSeconds = Math.Clamp(settings.SweepIntervalSeconds, 15, 3600);
        logger.LogInformation("Conciliación de Redsys y PayPal cada {IntervalSeconds} segundos.", intervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IProviderPaymentReconciliation>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("La conciliación de Redsys y PayPal no ha podido completarse. Tipo {ExceptionType}.", exception.GetType().Name);
            }
        }
    }
}
