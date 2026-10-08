using System.Net;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class AdminCatalogFilterTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Finds_products_by_name_reference_or_description()
    {
        var category = await AddCategoryAsync("Tintos", "tintos");
        var now = DateTimeOffset.UtcNow;
        await AddProductAsync(category.Id, "ALM-01", "Rufete Alma", "Notas de jara", ProductKind.Wine, true, now);
        await AddProductAsync(category.Id, "PAK-01", "Pack Dúo", "Caja de dos", ProductKind.Pack, true, now);
        await AddProductAsync(category.Id, "STD-01", "Copa", "Cristal de bodega", ProductKind.Standard, false, now);
        var longName = new string('a', 200);
        await AddProductAsync(category.Id, "LARGO", longName, "Descripción larga", ProductKind.Standard, true, now);

        var byName = await ProductsAsync(new AdminProductListQuery("  rufete  ", null, null, null));
        var byReference = await ProductsAsync(new AdminProductListQuery("pak-01", null, null, null));
        var byDescription = await ProductsAsync(new AdminProductListQuery("JARA", null, null, null));
        var none = await ProductsAsync(new AdminProductListQuery("no-existe", null, null, null));
        var truncated = await ProductsAsync(new AdminProductListQuery(new string('a', 250), null, null, null));

        Assert.Equal(["ALM-01"], References(byName));
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal(["PAK-01"], References(byReference));
        Assert.Equal(["ALM-01"], References(byDescription));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
        Assert.Equal(["LARGO"], References(truncated));
        Assert.Equal(1, truncated.TotalCount);
    }

    [Fact]
    public async Task Combines_product_filters_and_keeps_kind_status_and_order()
    {
        var tintos = await AddCategoryAsync("Tintos", "tintos");
        var blancos = await AddCategoryAsync("Blancos", "blancos");
        var older = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newer = older.AddMinutes(5);
        await AddProductAsync(tintos.Id, "ALM-01", "Rufete Alma", "Notas de jara", ProductKind.Wine, true, newer);
        await AddProductAsync(tintos.Id, "PAK-01", "Pack Dúo", "Caja", ProductKind.Pack, true, older);
        await AddProductAsync(tintos.Id, "STD-01", "Copa", "Cristal", ProductKind.Standard, false, older);
        await AddProductAsync(blancos.Id, "ALM-02", "Rufete Blanco", "Fresco", ProductKind.Wine, true, older);
        await AddProductAsync(tintos.Id, "ALF-01", "Alfa", "Lista", ProductKind.Standard, true, older);
        await AddProductAsync(tintos.Id, "BET-01", "Beta", "Lista", ProductKind.Standard, true, older);

        var combined = await ProductsAsync(new AdminProductListQuery("rufete", tintos.Id, ProductKind.Wine, true));
        var packs = await ProductsAsync(new AdminProductListQuery(null, null, ProductKind.Pack, null));
        var standards = await ProductsAsync(new AdminProductListQuery(null, tintos.Id, ProductKind.Standard, null));
        var inactive = await ProductsAsync(new AdminProductListQuery(null, null, null, false));
        var activeTintos = await ProductsAsync(new AdminProductListQuery(null, tintos.Id, null, true));
        var ordered = await ProductsAsync(AdminProductListQuery.Unfiltered);

        Assert.Equal(["ALM-01"], References(combined));
        Assert.Equal(1, combined.TotalCount);
        Assert.Equal(["PAK-01"], References(packs));
        Assert.Equal(["ALF-01", "BET-01", "STD-01"], References(standards));
        Assert.Equal(3, standards.TotalCount);
        Assert.Equal(["STD-01"], References(inactive));
        Assert.Equal(4, activeTintos.TotalCount);
        Assert.DoesNotContain(activeTintos.Items, item => item.Reference == "STD-01" || item.Reference == "ALM-02");
        Assert.Equal(["ALM-01", "ALF-01", "BET-01", "STD-01", "PAK-01", "ALM-02"], References(ordered));
        Assert.Equal(ordered.Items.Count, ordered.TotalCount);
    }

    [Fact]
    public async Task Treats_percent_underscore_and_backslash_as_literal_text()
    {
        var category = await AddCategoryAsync("Signos", "signos");
        var now = DateTimeOffset.UtcNow;
        await AddProductAsync(category.Id, "LIT_1", "100%", "ruta\\sur", ProductKind.Wine, true, now);
        await AddProductAsync(category.Id, "LIMPIO", "Limpio", "sin signos", ProductKind.Standard, true, now);

        var percent = await ProductsAsync(new AdminProductListQuery("%", null, null, null));
        var underscore = await ProductsAsync(new AdminProductListQuery("_", null, null, null));
        var slash = await ProductsAsync(new AdminProductListQuery("\\", null, null, null));

        Assert.Equal(["LIT_1"], References(percent));
        Assert.Equal(1, percent.TotalCount);
        Assert.Equal(["LIT_1"], References(underscore));
        Assert.Equal(["LIT_1"], References(slash));
    }

    [Fact]
    public async Task Finds_categories_by_name_or_slug_and_filters_parent_and_status()
    {
        var root = await AddCategoryAsync("Raiz", "raiz");
        var child = await AddCategoryAsync("Hija publica", "hija-publica", root.Id);
        await AddCategoryAsync("Nieta publica", "nieta-publica", child.Id);
        await AddCategoryAsync("Suelta", "suelta-oculta", null, false);

        var byName = await CategoriesAsync(new AdminCategoryListQuery("HIJA", null, null, false));
        var bySlug = await CategoriesAsync(new AdminCategoryListQuery("nieta-publica", null, null, false));
        var children = await CategoriesAsync(new AdminCategoryListQuery(null, true, root.Id, false));
        var roots = await CategoriesAsync(new AdminCategoryListQuery(null, null, null, true));
        var inactive = await CategoriesAsync(new AdminCategoryListQuery(null, false, null, false));
        var none = await CategoriesAsync(new AdminCategoryListQuery("no-existe", null, null, false));
        var all = await CategoriesAsync(AdminCategoryListQuery.Unfiltered);

        Assert.Equal(["Hija publica"], Names(byName));
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal(["Nieta publica"], Names(bySlug));
        Assert.Equal(["Hija publica"], Names(children));
        Assert.Equal(["Raiz", "Suelta"], Names(roots));
        Assert.Equal(2, roots.TotalCount);
        Assert.Equal(["Suelta"], Names(inactive));
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(["Hija publica", "Nieta publica", "Raiz", "Suelta"], Names(all));
    }

    [Fact]
    public async Task Edit_forms_keep_every_category_when_the_list_is_filtered()
    {
        await TestCatalog.SeedAsync();
        Guid visibleId;
        Guid hiddenId;
        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var visible = new Category(Guid.NewGuid(), "Visible filtro", "visible-filtro", null, true);
            var hidden = new Category(Guid.NewGuid(), "Ajena al filtro", "ajena-filtro", null, false);
            var now = DateTimeOffset.UtcNow;
            db.Categories.AddRange(visible, hidden);
            db.Products.Add(Product(visible.Id, "VIS-01", "Rufete de prueba", "Ficha", ProductKind.Standard, true, now));
            await db.SaveChangesAsync();
            visibleId = visible.Id;
            hiddenId = hidden.Id;
        }

        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);
        var page = await client.GetAsync(
            "/admin?categoryQuery=Visible&productQuery=rufete&productKind=no-es-tipo&productCategory=no-guid&categoryParent=tampoco&productActive=quizas");
        var html = await page.Content.ReadAsStringAsync();
        var clearProducts = Regex.Match(html, "id=\"clear-product-filters\" href=\"([^\"]+)\"").Groups[1].Value;
        var clearCategories = Regex.Match(html, "id=\"clear-category-filters\" href=\"([^\"]+)\"").Groups[1].Value;
        var imageForm = Regex.Match(html, "action=\"([^\"]*handler=SetPrimaryImage[^\"]*)\"").Groups[1].Value;

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"data-id=\"{visibleId}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-id=\"{hiddenId}\"", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{visibleId}\"", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{hiddenId}\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"edit-category\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"category-modal\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Visible\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"rufete\"", html, StringComparison.Ordinal);
        Assert.Contains("categoryQuery=Visible", clearProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("productQuery", clearProducts, StringComparison.Ordinal);
        Assert.Contains("productQuery=rufete", clearCategories, StringComparison.Ordinal);
        Assert.DoesNotContain("categoryQuery", clearCategories, StringComparison.Ordinal);
        Assert.Contains("productQuery=rufete", imageForm, StringComparison.Ordinal);
        Assert.Contains("categoryQuery=Visible", imageForm, StringComparison.Ordinal);
        Assert.Contains("window.location.assign(window.location.href)", html, StringComparison.Ordinal);
        Assert.Contains("class=\"edit-button js-toggle-category\"", html, StringComparison.Ordinal);
        Assert.Contains("Desactivar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Toggles_category_visibility_without_changing_descendants_and_respects_roles()
    {
        await TestCatalog.SeedAsync();
        var root = await AddCategoryAsync("Raiz publica", "raiz-publica");
        var child = await AddCategoryAsync("Hija publica", "hija-publica", root.Id);
        var now = DateTimeOffset.UtcNow;
        await AddProductAsync(child.Id, "HIJA1", "Vino de la hija", "Notas", ProductKind.Wine, true, now);

        using var manager = CreateClient();
        await LoginAsync(manager, TestAccounts.ManagerUsername);
        var page = await manager.GetAsync("/admin");
        var html = await page.Content.ReadAsStringAsync();
        var deactivated = await PostCategoryAsync(manager, html, root, false);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Contains("\"ok\":true", await deactivated.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using (var scope = Fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Categories.Where(category => category.Id == root.Id).Select(category => category.Active).SingleAsync());
            Assert.True(await db.Categories.Where(category => category.Id == child.Id).Select(category => category.Active).SingleAsync());
        }

        var hiddenCategories = await manager.GetStringAsync("/api/v1/categories");
        var hiddenProducts = await manager.GetStringAsync("/api/v1/products?page=1&pageSize=50");
        Assert.DoesNotContain("hija-publica", hiddenCategories, StringComparison.Ordinal);
        Assert.DoesNotContain("HIJA1", hiddenProducts, StringComparison.Ordinal);

        var refreshed = await manager.GetAsync("/admin?categoryQuery=hija");
        var refreshedHtml = await refreshed.Content.ReadAsStringAsync();
        var reactivatedChild = await PostCategoryAsync(manager, refreshedHtml, child, true);
        Assert.Equal(HttpStatusCode.OK, reactivatedChild.StatusCode);
        var stillHidden = await manager.GetStringAsync("/api/v1/categories");
        Assert.DoesNotContain("hija-publica", stillHidden, StringComparison.Ordinal);

        var restored = await PostCategoryAsync(manager, refreshedHtml, root, true);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var visible = await manager.GetStringAsync("/api/v1/categories");
        Assert.Contains("hija-publica", visible, StringComparison.Ordinal);

        using var viewer = CreateClient();
        await LoginAsync(viewer, TestAccounts.ViewerUsername);
        var viewerPage = await viewer.GetAsync("/admin");
        var viewerHtml = await viewerPage.Content.ReadAsStringAsync();
        var viewerToken = Regex.Match(viewerHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var denied = await viewer.PostAsync("/admin?handler=UpdateCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["categoryId"] = root.Id.ToString(),
            ["InputCategory.Name"] = root.Name,
            ["InputCategory.Slug"] = root.Slug,
            ["InputCategory.Active"] = "false",
            ["__RequestVerificationToken"] = viewerToken
        }));
        var missingToken = await manager.PostAsync("/admin?handler=UpdateCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["categoryId"] = root.Id.ToString(),
            ["InputCategory.Name"] = root.Name,
            ["InputCategory.Slug"] = root.Slug,
            ["InputCategory.Active"] = "false"
        }));

        Assert.DoesNotContain("class=\"edit-button js-toggle-category\"", viewerHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
    }

    [Fact]
    public async Task Finds_a_pack_by_the_name_of_a_contained_product()
    {
        var tintos = await AddCategoryAsync("Tintos", "tintos");
        var blancos = await AddCategoryAsync("Blancos", "blancos");
        var now = DateTimeOffset.UtcNow;
        var blanco = await AddProductAsync(blancos.Id, "BLANCO1", "Dominio de la Sierra Blanco 2024", "Fresco", ProductKind.Wine, true, now);
        var otro = await AddProductAsync(blancos.Id, "BLANCO2", "Otro blanco de parcela", "Fruta", ProductKind.Wine, true, now);
        var cosecha = await AddProductAsync(tintos.Id, "COSECHA", "100%_marca", "Sin pack en el nombre", ProductKind.Wine, true, now);
        var pack = await AddProductAsync(tintos.Id, "CAJA1", "Caja de altura", "Selección de la sierra", ProductKind.Pack, true, now);
        var lote = await AddProductAsync(blancos.Id, "LOTE1", "Lote norte", "Sin porcentaje", ProductKind.Pack, true, now);
        var decoy = await AddProductAsync(tintos.Id, "CAJA2", "Caja tinta", "Solo tinto", ProductKind.Pack, true, now);
        await AddComponentAsync(pack, blanco);
        await AddComponentAsync(pack, otro);
        await AddComponentAsync(lote, cosecha);

        var packs = await ProductsAsync(new AdminProductListQuery("blanco", null, ProductKind.Pack, null));
        var wines = await ProductsAsync(new AdminProductListQuery("blanco", null, ProductKind.Wine, null));
        var combined = await ProductsAsync(new AdminProductListQuery("blanco", tintos.Id, ProductKind.Pack, true));
        var all = await ProductsAsync(new AdminProductListQuery("blanco", null, null, null));
        var none = await ProductsAsync(new AdminProductListQuery("no-hay-coincidencias", null, ProductKind.Pack, null));
        var literal = await ProductsAsync(new AdminProductListQuery("%", null, ProductKind.Pack, null));
        var underscore = await ProductsAsync(new AdminProductListQuery("_", null, null, null));

        Assert.Equal(["CAJA1"], References(packs));
        Assert.Equal(1, packs.TotalCount);
        Assert.Equal(["BLANCO1", "BLANCO2"], References(wines).OrderBy(item => item).ToArray());
        Assert.DoesNotContain(wines.Items, item => item.Kind == ProductKind.Pack);
        Assert.Equal(["CAJA1"], References(combined));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(3, all.Items.Select(item => item.Id).Distinct().Count());
        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
        Assert.Equal(["LOTE1"], References(literal));
        Assert.Equal(2, underscore.TotalCount);
        Assert.Equal(["COSECHA", "LOTE1"], References(underscore).OrderBy(item => item).ToArray());
    }

    [Fact]
    public async Task Refreshes_filtered_rows_without_replacing_category_options()
    {
        await TestCatalog.SeedAsync();
        var visible = await AddCategoryAsync("Visible filtro", "visible-filtro");
        var hidden = await AddCategoryAsync("Ajena al filtro", "ajena-filtro");
        using var client = CreateClient();
        await LoginAsync(client, TestAccounts.AdminUsername);

        var rows = await client.GetAsync("/admin?handler=CategoryRows&categoryQuery=Visible");
        var fragment = await rows.Content.ReadAsStringAsync();
        var page = await client.GetStringAsync("/admin?categoryQuery=Visible");

        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        Assert.Contains("id=\"category-fragment\"", fragment, StringComparison.Ordinal);
        Assert.Contains("data-count=\"1\"", fragment, StringComparison.Ordinal);
        Assert.Contains(visible.Name, fragment, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Name, fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"category-parent-filter\"", fragment, StringComparison.Ordinal);
        Assert.Contains($"value=\"{hidden.Id}\"", page, StringComparison.Ordinal);
        Assert.Contains($"value=\"{visible.Id}\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"category-modal\"", page, StringComparison.Ordinal);
        Assert.Contains("admin-live-filters.js", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serves_the_store_favicon()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/favicon.svg?v=ds-signature-4");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", body, StringComparison.Ordinal);
        Assert.Contains("Dominio de la Sierra", body, StringComparison.Ordinal);
    }

    private async Task<AdminListResult<AdminProductDto>> ProductsAsync(AdminProductListQuery query)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAdminCatalogQueries>()
            .GetProductsAsync(query);
    }

    private async Task<AdminListResult<AdminCategoryDto>> CategoriesAsync(AdminCategoryListQuery query)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAdminCatalogQueries>()
            .GetCategoriesAsync(query);
    }

    private async Task<Category> AddCategoryAsync(string name, string slug, Guid? parentId = null, bool active = true)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new Category(Guid.NewGuid(), name, slug, parentId, active);
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private async Task<Guid> AddProductAsync(
        Guid categoryId,
        string reference,
        string name,
        string description,
        ProductKind kind,
        bool active,
        DateTimeOffset updatedAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = Product(categoryId, reference, name, description, kind, active, updatedAt);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task AddComponentAsync(Guid packId, Guid componentId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ProductComponents.Add(new ProductComponent(Guid.NewGuid(), packId, componentId, 1));
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PostCategoryAsync(
        HttpClient client,
        string html,
        Category category,
        bool active)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/admin?handler=UpdateCategory", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["categoryId"] = category.Id.ToString(),
            ["InputCategory.Name"] = category.Name,
            ["InputCategory.Slug"] = category.Slug,
            ["InputCategory.ParentCategoryId"] = category.ParentCategoryId?.ToString() ?? string.Empty,
            ["InputCategory.Active"] = active ? "true" : "false",
            ["__RequestVerificationToken"] = token
        }));
    }

    private static Product Product(
        Guid categoryId,
        string reference,
        string name,
        string description,
        ProductKind kind,
        bool active,
        DateTimeOffset updatedAt)
    {
        return new Product(
            Guid.NewGuid(),
            reference,
            name,
            reference.ToLowerInvariant(),
            description,
            categoryId,
            10m,
            21m,
            active,
            updatedAt,
            updatedAt,
            kind);
    }

    private static string[] References(AdminListResult<AdminProductDto> result) =>
        result.Items.Select(item => item.Reference).ToArray();

    private static string[] Names(AdminListResult<AdminCategoryDto> result) =>
        result.Items.Select(item => item.Name).ToArray();

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
}
