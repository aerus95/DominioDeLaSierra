using DominioDeLaSierra.Api.Errors;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure;
using DominioDeLaSierra.Infrastructure.Media;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keysPath))
{
    keysPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DominioDeLaSierra",
        "dpkeys");
}

Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("DominioDeLaSierra");

builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection(AdminBootstrapOptions.SectionName));
builder.Services.AddScoped<AdminCredentialVerifier>();
builder.Services.AddHostedService<AdminBootstrapHostedService>();

builder.Services.AddAuthentication(AdminAuthOptions.Scheme)
    .AddCookie(AdminAuthOptions.Scheme, options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
            RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context =>
            RedirectOrStatus(context, StatusCodes.Status403Forbidden);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAuthOptions.PanelPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AdminAuthOptions.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(
            nameof(AdminRole.Administrator),
            nameof(AdminRole.Manager),
            nameof(AdminRole.Viewer));
    });
    options.AddPolicy(AdminAuthOptions.WritePolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AdminAuthOptions.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(nameof(AdminRole.Administrator), nameof(AdminRole.Manager));
    });
    options.AddPolicy(AdminAuthOptions.UserManagementPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AdminAuthOptions.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(nameof(AdminRole.Administrator));
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Nginx reaches the container through the published port, not as a loopback peer.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", AdminAuthOptions.PanelPolicy);
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

var testingMediaRoot = default(string);
if (app.Environment.IsEnvironment("Testing"))
{
    var databaseName = builder.Configuration["Database:Name"] ?? string.Empty;
    var databaseHost = builder.Configuration["Database:Host"] ?? "localhost";
    if (!databaseName.EndsWith("_tests", StringComparison.OrdinalIgnoreCase)
        || databaseHost is not ("localhost" or "127.0.0.1"))
    {
        throw new InvalidOperationException(
            "El entorno Testing solo puede usar una base local cuyo nombre termina en _tests.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["Media:RootPath"]))
    {
        testingMediaRoot = Path.Combine(Path.GetTempPath(), "dominio-sierra-tests", "media");
    }
}

app.UseForwardedHeaders();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else if (app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler("/error");
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHttpsRedirection();
}

app.UseMiddleware<NotFoundResponseMiddleware>();
app.UseRouting();

app.UseCors("Frontend");

var mediaRoot = testingMediaRoot
    ?? ProductMediaOptions.Resolve(builder.Configuration["Media:RootPath"]);
Directory.CreateDirectory(mediaRoot);
var mediaContentTypes = new FileExtensionContentTypeProvider();
mediaContentTypes.Mappings[".webp"] = "image/webp";
mediaContentTypes.Mappings.Remove(".svg");
mediaContentTypes.Mappings.Remove(".svgz");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaRoot),
    RequestPath = "/media",
    ContentTypeProvider = mediaContentTypes,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    }
});

app.UseAuthentication();
app.UseAuthorization();
var faviconPath = FindStoreFavicon(app.Environment.ContentRootPath) ?? FindStoreFavicon(AppContext.BaseDirectory);
if (faviconPath is not null)
{
    app.MapGet("/favicon.svg", () => Results.File(faviconPath, "image/svg+xml"));
}

app.MapRazorPages();
app.MapControllers();
app.Run();

static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int statusCode)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}

static string? FindStoreFavicon(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "Angular", "web", "src", "favicon.svg");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

public partial class Program;
