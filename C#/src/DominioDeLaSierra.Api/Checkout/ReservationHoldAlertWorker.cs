using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Api.Checkout;

/// <summary>
/// Revisa reservas retenidas sin liberar stock. HoldAlertEnabled lo apaga.
/// El barrido que sí puede liberar reservas de Stripe sigue dependiendo de SweepEnabled.
/// </summary>
public sealed class ReservationHoldAlertWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSweepOptions> options,
    ILogger<ReservationHoldAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.HoldAlertEnabled)
        {
            logger.LogInformation("Alertas de reservas retenidas desactivadas.");
            return;
        }

        var intervalSeconds = Math.Clamp(settings.SweepIntervalSeconds, 15, 3600);
        logger.LogInformation(
            "Alertas de reservas retenidas cada {IntervalSeconds} segundos, umbral de {HoldAlertHours} horas.",
            intervalSeconds,
            settings.HoldAlertHours);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReservationHoldAlerts>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("La revisión de reservas retenidas no ha podido completarse. Tipo {ExceptionType}.", exception.GetType().Name);
            }
        }
    }
}
