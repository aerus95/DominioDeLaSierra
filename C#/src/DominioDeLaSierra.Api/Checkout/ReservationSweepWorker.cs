using DominioDeLaSierra.Application.Checkout;
using Microsoft.Extensions.Options;

namespace DominioDeLaSierra.Api.Checkout;

/// <summary>
/// Cada Reservations:SweepIntervalSeconds (60 por defecto) revisa un lote de reservas vencidas.
/// Reservations:SweepEnabled lo apaga. En pruebas queda apagado; el valor por defecto del código también es false,
/// así que no arranca en producción hasta que se active la sección Reservations.
/// </summary>
public sealed class ReservationSweepWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationSweepOptions> options,
    ILogger<ReservationSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SweepEnabled)
        {
            logger.LogInformation("Barrido de reservas desactivado.");
            return;
        }

        var intervalSeconds = Math.Clamp(settings.SweepIntervalSeconds, 15, 3600);
        var batch = Math.Clamp(settings.BatchSize, 1, 100);
        logger.LogInformation(
            "Barrido de reservas cada {IntervalSeconds} segundos, lotes de {BatchSize}.",
            intervalSeconds,
            batch);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReservationSweep>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("El barrido de reservas no ha podido completarse. Tipo {ExceptionType}.", exception.GetType().Name);
            }
        }
    }
}
