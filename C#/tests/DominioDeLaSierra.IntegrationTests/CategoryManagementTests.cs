using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Infrastructure.Categories;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.DeleteCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Categories.UpdateCategory;
using DominioDeLaSierra.Application.Products.GetProducts;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class CategoryManagementTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Updates_name_slug_parent_and_active_state()
    {
        var root = await CreateAsync("Raiz");
        var child = await CreateAsync("Hija", root.Id);

        var updated = await UpdateAsync(child.Id, "Hija nueva", "hija-nueva", null, false);

        Assert.Equal("Hija nueva", updated.Name);
        Assert.Equal("hija-nueva", updated.Slug);
        Assert.Null(updated.ParentCategoryId);
        Assert.False(updated.Active);

        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Categories.SingleAsync(category => category.Id == child.Id);
            Assert.Equal("Hija nueva", stored.Name);
            Assert.Equal("hija-nueva", stored.Slug);
            Assert.Null(stored.ParentCategoryId);
            Assert.False(stored.Active);

            var visible = await scope.ServiceProvider.GetRequiredService<ICategoryCatalogQueries>().GetCategoriesAsync();
            Assert.DoesNotContain(visible, category => category.Id == child.Id);
            Assert.Contains(visible, category => category.Id == root.Id);
        }

        var reactivated = await UpdateAsync(child.Id, "Hija nueva", "hija-nueva", root.Id, true);

        Assert.True(reactivated.Active);
        Assert.Equal(root.Id, reactivated.ParentCategoryId);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var visible = await scope.ServiceProvider.GetRequiredService<ICategoryCatalogQueries>().GetCategoriesAsync();
            Assert.Contains(visible, category => category.Id == child.Id && category.ParentCategoryId == root.Id);
        }
    }

    [Fact]
    public async Task Rejects_duplicate_slug()
    {
        var first = await CreateAsync("Tintos");
        var second = await CreateAsync("Blancos");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateAsync(second.Id, "Blancos", first.Slug, null, true));

        Assert.Equal(CatalogConflicts.DuplicateCategorySlug, exception.Message);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
            .SingleAsync(category => category.Id == second.Id);
        Assert.Equal(second.Slug, stored.Slug);
    }

    [Fact]
    public async Task Rejects_missing_parent_and_unknown_category()
    {
        var category = await CreateAsync("Tintos");

        var missingParent = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateAsync(category.Id, "Tintos", category.Slug, Guid.NewGuid(), true));
        var missingCategory = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateAsync(Guid.NewGuid(), "Nadie", null, null, true));

        Assert.Equal(CatalogConflicts.UnavailableParentCategory, missingParent.Message);
        Assert.Equal(CatalogConflicts.CategoryNotFound, missingCategory.Message);
    }

    [Fact]
    public async Task Rejects_self_reference_and_cycles()
    {
        var root = await CreateAsync("Raiz");
        var child = await CreateAsync("Hija", root.Id);
        var grandchild = await CreateAsync("Nieta", child.Id);

        var self = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateAsync(root.Id, root.Name, root.Slug, root.Id, true));
        var childCycle = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateAsync(root.Id, root.Name, root.Slug, child.Id, true));
        var grandchildCycle = await Assert.ThrowsAsync<ArgumentException>(() =>
            UpdateAsync(root.Id, root.Name, root.Slug, grandchild.Id, true));

        Assert.Equal(CatalogConflicts.CategorySelfParent, self.Message);
        Assert.Equal(CatalogConflicts.CategoryCycle, childCycle.Message);
        Assert.Equal(CatalogConflicts.CategoryCycle, grandchildCycle.Message);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
            .SingleAsync(category => category.Id == root.Id);
        Assert.Null(stored.ParentCategoryId);
    }

    [Fact]
    public async Task Concurrent_parent_updates_cannot_form_a_cycle()
    {
        var firstCategory = await CreateAsync("Ciclo uno");
        var secondCategory = await CreateAsync("Ciclo dos");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CategoryHierarchyLock.BeforeSave = _ =>
        {
            entered.TrySetResult();
            return release.Task;
        };

        Task<Exception?>? first = null;
        Task<Exception?>? second = null;
        try
        {
            first = CaptureAsync(UpdateAsync(
                firstCategory.Id,
                firstCategory.Name,
                firstCategory.Slug,
                secondCategory.Id,
                true));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = CaptureAsync(UpdateAsync(
                secondCategory.Id,
                secondCategory.Name,
                secondCategory.Slug,
                firstCategory.Id,
                true));

            var sawWaiter = false;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !second.IsCompleted)
            {
                if (await CountHierarchyLockWaitersAsync() > 0)
                {
                    sawWaiter = true;
                    break;
                }

                await Task.Delay(40);
            }

            release.TrySetResult();
            var firstError = await first;
            var secondError = await second;
            var errors = new[] { firstError, secondError }.Where(error => error is not null).ToArray();
            var error = Assert.Single(errors);
            var cycle = Assert.IsType<ArgumentException>(error);

            Assert.True(sawWaiter);
            Assert.Equal(CatalogConflicts.CategoryCycle, cycle.Message);
            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            var links = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
                .AsNoTracking()
                .Select(category => new { category.Id, category.ParentCategoryId })
                .ToDictionaryAsync(category => category.Id, category => category.ParentCategoryId);
            Assert.False(ContainsCycle(links));
            Assert.True((links[firstCategory.Id] == secondCategory.Id) ^ (links[secondCategory.Id] == firstCategory.Id));
        }
        finally
        {
            release.TrySetResult();
            CategoryHierarchyLock.BeforeSave = null;
            if (first is { IsCompleted: false })
            {
                await first;
            }

            if (second is { IsCompleted: false })
            {
                await second;
            }
        }
    }

    [Fact]
    public async Task Hides_descendants_of_an_inactive_category()
    {
        var root = await CreateAsync("Raiz publica");
        var child = await CreateAsync("Hija publica", root.Id);
        var grandchild = await CreateAsync("Nieta publica", child.Id);
        var loose = await CreateAsync("Suelta publica");
        await AddProductAsync(root.Id, "RAIZ1", true);
        await AddProductAsync(grandchild.Id, "NIETA1", true);
        await AddProductAsync(loose.Id, "SUELTA1", true);

        await AssertPublicCatalogAsync(
            [root.Id, child.Id, grandchild.Id, loose.Id],
            ["raiz1", "nieta1", "suelta1"],
            grandchild.Slug,
            "nieta1",
            grandchildVisible: true);

        await UpdateAsync(child.Id, child.Name, child.Slug, root.Id, false);

        await AssertPublicCatalogAsync(
            [root.Id, loose.Id],
            ["raiz1", "suelta1"],
            grandchild.Slug,
            "nieta1",
            grandchildVisible: false);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var admin = await scope.ServiceProvider.GetRequiredService<IAdminCatalogQueries>()
                .GetCategoriesAsync(AdminCategoryListQuery.Unfiltered);
            Assert.Contains(admin.Items, category => category.Id == child.Id && !category.Active);
            Assert.Contains(admin.Items, category => category.Id == grandchild.Id && category.Active);
        }

        await UpdateAsync(child.Id, child.Name, child.Slug, root.Id, true);
        await UpdateAsync(root.Id, root.Name, root.Slug, null, false);

        await AssertPublicCatalogAsync(
            [loose.Id],
            ["suelta1"],
            root.Slug,
            "raiz1",
            grandchildVisible: false);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var categories = await scope.ServiceProvider.GetRequiredService<ICategoryCatalogQueries>().GetCategoriesAsync();
            Assert.DoesNotContain(categories, category => category.Id == child.Id || category.Id == grandchild.Id);
            var products = scope.ServiceProvider.GetRequiredService<IProductCatalogQueries>();
            Assert.Null(await products.GetProductBySlugAsync(GetProductBySlugQuery.Create("nieta1")));
            var filtered = await products.GetProductsAsync(GetProductsQuery.Create(pageSize: 20, category: grandchild.Slug));
            Assert.Equal(0, filtered.TotalItems);
        }
    }

    [Fact]
    public async Task Deletes_a_category_without_products_or_children()
    {
        var category = await CreateAsync("Temporal");

        var deleted = await DeleteAsync(category.Id);

        Assert.Equal(category.Name, deleted.Name);
        var missing = await Assert.ThrowsAsync<ArgumentException>(() => DeleteAsync(category.Id));
        Assert.Equal(CatalogConflicts.CategoryNotFound, missing.Message);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var exists = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
            .AnyAsync(item => item.Id == category.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task Rejects_delete_when_products_exist()
    {
        var activeCategory = await CreateAsync("Con activo");
        var inactiveCategory = await CreateAsync("Con inactivo");
        await AddProductAsync(activeCategory.Id, "ACT-1", true);
        await AddProductAsync(inactiveCategory.Id, "INA-1", false);

        var activeError = await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(activeCategory.Id));
        var inactiveError = await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(inactiveCategory.Id));

        Assert.Equal(CatalogConflicts.CategoryHasProducts, activeError.Message);
        Assert.Equal(CatalogConflicts.CategoryHasProducts, inactiveError.Message);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
            .Select(category => category.Id)
            .ToListAsync();
        Assert.Contains(activeCategory.Id, ids);
        Assert.Contains(inactiveCategory.Id, ids);
    }

    [Fact]
    public async Task Rejects_delete_when_a_subcategory_exists()
    {
        var parent = await CreateAsync("Padre");
        var child = await CreateAsync("Hija", parent.Id);

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(parent.Id));

        Assert.Equal(CatalogConflicts.CategoryHasChildren, blocked.Message);
        await DeleteAsync(child.Id);
        await DeleteAsync(parent.Id);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var remaining = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories.CountAsync();
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task Viewer_cannot_update_or_delete_categories()
    {
        await TestCatalog.SeedAsync();
        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ViewerUsername);

        var page = await client.GetAsync("/admin");
        var html = await page.Content.ReadAsStringAsync();
        var update = await PostCategoryChangeAsync(client, html, "/admin?handler=UpdateCategory", new Dictionary<string, string>
        {
            ["categoryId"] = Guid.NewGuid().ToString(),
            ["InputCategory.Name"] = "No permitida",
            ["InputCategory.Active"] = "true"
        });
        var delete = await PostCategoryChangeAsync(client, html, "/admin?handler=DeleteCategory", new Dictionary<string, string>
        {
            ["categoryId"] = Guid.NewGuid().ToString()
        });
        var apiUpdate = await client.PutAsync(
            $"/api/v1/categories/{Guid.NewGuid()}",
            Json("""{"name":"No permitida","active":true}"""));
        var apiDelete = await client.DeleteAsync($"/api/v1/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(TestAccounts.CategoryName, html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"open-category\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"edit-button js-edit-category\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"edit-button danger js-delete-category\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"category-modal\"", html, StringComparison.Ordinal);
        await AssertForbiddenAsync(update);
        await AssertForbiddenAsync(delete);
        Assert.Equal(HttpStatusCode.Forbidden, apiUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, apiDelete.StatusCode);
    }

    [Fact]
    public async Task Manager_can_update_and_delete_categories_from_the_panel_and_the_api()
    {
        await TestCatalog.SeedAsync();
        Guid seededId;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            seededId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
                .Select(category => category.Id)
                .SingleAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.ManagerUsername);
        var page = await client.GetAsync("/admin");
        var html = await page.Content.ReadAsStringAsync();
        var update = await PostCategoryChangeAsync(client, html, "/admin?handler=UpdateCategory", new Dictionary<string, string>
        {
            ["categoryId"] = seededId.ToString(),
            ["InputCategory.Name"] = "E2E Editada",
            ["InputCategory.Slug"] = "e2e-editada",
            ["InputCategory.Active"] = "true"
        });

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("js-edit-category", html, StringComparison.Ordinal);
        Assert.Contains("js-delete-category", html, StringComparison.Ordinal);
        Assert.Contains(
            "Cambiar el slug puede afectar a los filtros del catálogo público y a las importaciones de productos.",
            html,
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Contains("\"ok\":true", await update.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories
                .SingleAsync(category => category.Id == seededId);
            Assert.Equal("E2E Editada", stored.Name);
            Assert.Equal("e2e-editada", stored.Slug);
            Assert.True(stored.Active);
        }

        var created = await client.PostAsync(
            "/api/v1/categories",
            Json("""{"name":"Categoria API","slug":"categoria-api","active":true}"""));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(createdBody);
        var createdId = createdJson.RootElement.GetProperty("id").GetGuid();

        var renamed = await client.PutAsync(
            $"/api/v1/categories/{createdId}",
            Json("""{"name":"Categoria API editada","slug":"categoria-api-editada","active":false}"""));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var catalog = await client.GetStringAsync("/api/v1/categories");
        Assert.DoesNotContain("categoria-api-editada", catalog, StringComparison.Ordinal);

        var reactivated = await client.PutAsync(
            $"/api/v1/categories/{createdId}",
            Json("""{"name":"Categoria API editada","slug":"categoria-api-editada","active":true}"""));
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        catalog = await client.GetStringAsync("/api/v1/categories");
        Assert.Contains("categoria-api-editada", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("\"active\"", catalog, StringComparison.Ordinal);

        var removed = await client.DeleteAsync($"/api/v1/categories/{createdId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        catalog = await client.GetStringAsync("/api/v1/categories");
        Assert.DoesNotContain("categoria-api-editada", catalog, StringComparison.Ordinal);

        var deleted = await PostCategoryChangeAsync(client, html, "/admin?handler=DeleteCategory", new Dictionary<string, string>
        {
            ["categoryId"] = seededId.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using (var check = Fixture.Factory.Services.CreateAsyncScope())
        {
            var remaining = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Categories.CountAsync();
            Assert.Equal(0, remaining);
        }
    }

    private async Task AssertPublicCatalogAsync(
        Guid[] visibleCategoryIds,
        string[] visibleProductSlugs,
        string filteredSlug,
        string hiddenOrVisibleProductSlug,
        bool grandchildVisible)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryCatalogQueries>().GetCategoriesAsync();
        var products = scope.ServiceProvider.GetRequiredService<IProductCatalogQueries>();
        var page = await products.GetProductsAsync(GetProductsQuery.Create(pageSize: 20));
        var filtered = await products.GetProductsAsync(GetProductsQuery.Create(pageSize: 20, category: filteredSlug));
        var bySlug = await products.GetProductBySlugAsync(GetProductBySlugQuery.Create(hiddenOrVisibleProductSlug));
        var components = await products.GetPackComponentsAsync(GetPackComponentsQuery.Create(hiddenOrVisibleProductSlug));

        Assert.Equal(visibleCategoryIds.OrderBy(id => id), categories.Select(category => category.Id).OrderBy(id => id));
        Assert.Equal(visibleProductSlugs.OrderBy(slug => slug), page.Items.Select(item => item.Slug).OrderBy(slug => slug));
        if (grandchildVisible)
        {
            Assert.Contains(filtered.Items, item => item.Slug == hiddenOrVisibleProductSlug);
            Assert.NotNull(bySlug);
            Assert.NotNull(components);
            Assert.Empty(components);
        }
        else
        {
            Assert.Empty(filtered.Items);
            Assert.Equal(0, filtered.TotalItems);
            Assert.Null(bySlug);
            Assert.Null(components);
        }
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task<int> CountHierarchyLockWaitersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var counts = await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT COUNT(*)::int AS "Value"
            FROM pg_locks
            WHERE locktype = 'advisory'
              AND classid = {CategoryHierarchyLock.Key1}
              AND objid = {CategoryHierarchyLock.Key2}
              AND NOT granted
            """)
            .ToListAsync();
        return counts.Single();
    }

    private static bool ContainsCycle(IReadOnlyDictionary<Guid, Guid?> parents)
    {
        foreach (var id in parents.Keys)
        {
            var seen = new HashSet<Guid>();
            var current = id;
            while (parents.TryGetValue(current, out var parent) && parent is Guid next)
            {
                if (!seen.Add(current))
                {
                    return true;
                }

                current = next;
            }

            if (!seen.Add(current))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<CreatedCategoryDto> CreateAsync(string name, Guid? parentId = null, bool active = true)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateCategory>().ExecuteAsync(
            new CreateCategoryCommand(name, null, parentId, active));
    }

    private async Task<UpdatedCategoryDto> UpdateAsync(Guid id, string name, string? slug, Guid? parentId, bool active)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUpdateCategory>().ExecuteAsync(
            new UpdateCategoryCommand(id, name, slug, parentId, active));
    }

    private async Task<DeletedCategoryDto> DeleteAsync(Guid id)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDeleteCategory>().ExecuteAsync(
            new DeleteCategoryCommand(id));
    }

    private async Task AddProductAsync(Guid categoryId, string reference, bool active)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Products.Add(new Product(
            Guid.NewGuid(),
            reference,
            reference,
            reference.ToLowerInvariant(),
            "Descripción de prueba",
            categoryId,
            12m,
            21m,
            active,
            now,
            now));
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClient()
    {
        var handler = new CookieHandler
        {
            InnerHandler = Fixture.Factory.Server.CreateHandler()
        };
        return new HttpClient(handler)
        {
            BaseAddress = Fixture.Factory.Server.BaseAddress
        };
    }

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/admin/login");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = TestAccounts.Password,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostCategoryChangeAsync(
        HttpClient client,
        string html,
        string url,
        Dictionary<string, string> fields)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("No tienes permiso para modificar el catálogo.", json.RootElement.GetProperty("message").GetString());
    }

    private static StringContent Json(string payload) =>
        new(payload, Encoding.UTF8, "application/json");
}
