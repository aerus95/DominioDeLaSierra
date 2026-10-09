using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DominioDeLaSierra.Infrastructure.Persistence;

/// <summary>
/// Un Payment con Guid asignado en el dominio no es una clave temporal.
/// Si se añade a un pedido ya seguido, EF lo marca como Modified y el UPDATE no afecta a ninguna fila.
/// Cuando ese identificador aún no existe, la inserción debe quedar explícitamente en Added.
/// </summary>
public sealed class TrackNewPaymentsInterceptor : SaveChangesInterceptor
{
    private static readonly AsyncLocal<bool> Running = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        PromoteNewPayments(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PromoteNewPaymentsAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void PromoteNewPayments(DbContext? context)
    {
        if (context is null || Running.Value)
        {
            return;
        }

        Running.Value = true;
        try
        {
            Promote<Payment>(context, static (db, id) => db.Set<Payment>().AsNoTracking().Any(item => item.Id == id));
            Promote<PaymentEvent>(context, static (db, id) => db.Set<PaymentEvent>().AsNoTracking().Any(item => item.Id == id));
            Promote<FulfillmentTransition>(context, static (db, id) => db.Set<FulfillmentTransition>().AsNoTracking().Any(item => item.Id == id));
        }
        finally
        {
            Running.Value = false;
        }
    }

    private static async Task PromoteNewPaymentsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || Running.Value)
        {
            return;
        }

        Running.Value = true;
        try
        {
            await PromoteAsync<Payment>(context, (db, id, token) => db.Set<Payment>().AsNoTracking().AnyAsync(item => item.Id == id, token), cancellationToken);
            await PromoteAsync<PaymentEvent>(context, (db, id, token) => db.Set<PaymentEvent>().AsNoTracking().AnyAsync(item => item.Id == id, token), cancellationToken);
            await PromoteAsync<FulfillmentTransition>(context, (db, id, token) => db.Set<FulfillmentTransition>().AsNoTracking().AnyAsync(item => item.Id == id, token), cancellationToken);
        }
        finally
        {
            Running.Value = false;
        }
    }

    private static void Promote<TEntity>(DbContext context, Func<DbContext, Guid, bool> exists)
        where TEntity : class
    {
        foreach (var entry in context.ChangeTracker.Entries<TEntity>().Where(item => item.State == EntityState.Modified).ToArray())
        {
            if (entry.Property("Id").CurrentValue is Guid id && !exists(context, id))
            {
                entry.State = EntityState.Added;
            }
        }
    }

    private static async Task PromoteAsync<TEntity>(
        DbContext context,
        Func<DbContext, Guid, CancellationToken, Task<bool>> exists,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        foreach (var entry in context.ChangeTracker.Entries<TEntity>().Where(item => item.State == EntityState.Modified).ToArray())
        {
            if (entry.Property("Id").CurrentValue is Guid id && !await exists(context, id, cancellationToken))
            {
                entry.State = EntityState.Added;
            }
        }
    }
}
