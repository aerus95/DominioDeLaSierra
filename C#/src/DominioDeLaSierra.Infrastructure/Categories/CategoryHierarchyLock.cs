using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

internal static class CategoryHierarchyLock
{
    public const int Key1 = 724318;
    public const int Key2 = 5301;

    public static Func<CancellationToken, Task>? BeforeSave { get; set; }

    public static Task AcquireAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({Key1}, {Key2})",
            cancellationToken);
}
