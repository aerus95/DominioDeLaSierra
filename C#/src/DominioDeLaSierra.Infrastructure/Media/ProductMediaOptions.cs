namespace DominioDeLaSierra.Infrastructure.Media;

public sealed class ProductMediaOptions
{
    public const string SectionName = "Media";
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxImagePixels = 16_000_000;
    public const int MinEdge = 800;
    public const int CanvasEdge = 1200;
    public const int WebpQuality = 82;

    public string? RootPath { get; set; }

    public string ResolveRootPath() => Resolve(RootPath);

    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DominioDeLaSierra",
            "media"));
    }
}
