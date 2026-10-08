using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Categories;

internal static class PublicCategoryVisibility
{
    public static async Task<List<Guid>> GetVisibleIdsAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Categories
            .AsNoTracking()
            .Select(category => new CategoryLink(category.Id, category.ParentCategoryId, category.Active))
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(row => row.Id);
        var visible = new List<Guid>(rows.Count);
        foreach (var row in rows)
        {
            if (IsVisible(row, byId))
            {
                visible.Add(row.Id);
            }
        }

        return visible;
    }

    private static bool IsVisible(CategoryLink row, Dictionary<Guid, CategoryLink> byId)
    {
        var seen = new HashSet<Guid>();
        var current = row;
        while (true)
        {
            if (!current.Active || !seen.Add(current.Id))
            {
                return false;
            }

            if (current.ParentCategoryId is not Guid parentId || !byId.TryGetValue(parentId, out var parent))
            {
                return current.ParentCategoryId is null;
            }

            current = parent;
        }
    }

    private sealed record CategoryLink(Guid Id, Guid? ParentCategoryId, bool Active);
}
