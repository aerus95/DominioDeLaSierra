using System.Reflection;
using PdfSharp.Fonts;

namespace DominioDeLaSierra.Infrastructure.Invoicing;

public sealed class InvoiceFontResolver : IFontResolver
{
    public const string FamilyName = "Segoe WP";
    private const string AssemblyName = "PdfSharp.WPFonts";
    private static readonly Lazy<byte[]> Regular = new(() => Load("PdfSharp.WPFonts.Fonts.SegoeWP.ttf"));
    private static readonly Lazy<byte[]> Bold = new(() => Load("PdfSharp.WPFonts.Fonts.SegoeWP-Bold.ttf"));

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = isBold ? "Invoice-Bold" : "Invoice-Regular";
        return new FontResolverInfo(face, mustSimulateBold: false, mustSimulateItalic: isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        return faceName == "Invoice-Bold" ? Bold.Value : Regular.Value;
    }

    private static byte[] Load(string resourceName)
    {
        var assembly = Assembly.Load(AssemblyName);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("No hay una fuente disponible para la factura.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
