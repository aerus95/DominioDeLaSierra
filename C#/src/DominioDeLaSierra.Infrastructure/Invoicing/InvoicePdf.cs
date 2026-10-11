using System.Globalization;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain.Entities;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DominioDeLaSierra.Infrastructure.Invoicing;

public static class InvoicePdf
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private static readonly XColor Wine = XColor.FromArgb(66, 31, 37);
    private static readonly XColor Ink = XColor.FromArgb(29, 25, 23);
    private static readonly XColor Gold = XColor.FromArgb(189, 149, 96);
    private static readonly XColor Cream = XColor.FromArgb(242, 237, 228);
    private static readonly XColor Muted = XColor.FromArgb(117, 108, 101);

    static InvoicePdf()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public static byte[] Render(Invoice invoice)
    {
        var family = ResolveFamily();
        using var document = new PdfDocument();
        document.Info.Title = invoice.Fictional ? "Documento de prueba" : "Factura " + invoice.FullNumber;
        document.Info.Author = invoice.SellerLegalName;
        document.Info.Subject = invoice.Fictional ? Invoice.FictionalBanner : "Factura";
        document.Options.NoCompression = true;
        document.Options.CompressContentStreams = false;

        var title = new XFont(family, 16, XFontStyleEx.Bold);
        var heading = new XFont(family, 11, XFontStyleEx.Bold);
        var regular = new XFont(family, 10, XFontStyleEx.Regular);
        var small = new XFont(family, 8, XFontStyleEx.Regular);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(Muted);
        var white = new XSolidBrush(XColors.White);

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var graphics = XGraphics.FromPdfPage(page);
        var y = DrawMasthead(graphics, page, invoice, title, small, white);

        y = DrawParties(graphics, invoice, heading, regular, ink, y);
        y = DrawFacts(graphics, invoice, heading, regular, ink, y);
        y = DrawTableHeader(graphics, page, heading, white, y);

        foreach (var line in invoice.Lines.OrderBy(item => item.Position))
        {
            if (y > page.Height.Point - 150)
            {
                graphics.Dispose();
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                graphics = XGraphics.FromPdfPage(page);
                y = DrawContinued(graphics, page, invoice, heading, white);
                y = DrawTableHeader(graphics, page, heading, white, y);
            }

            y = DrawLine(graphics, line, regular, ink, y);
        }

        y = DrawShipping(graphics, invoice, regular, ink, y);
        if (y > page.Height.Point - 190)
        {
            graphics.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            y = DrawContinued(graphics, page, invoice, heading, white);
        }

        DrawSummary(graphics, invoice, heading, regular, small, ink, muted, y);
        graphics.Dispose();
        DrawPageNumbers(document, small, muted);
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static double DrawMasthead(XGraphics graphics, PdfPage page, Invoice invoice, XFont title, XFont small, XBrush white)
    {
        graphics.DrawRectangle(new XSolidBrush(Wine), 0, 0, page.Width.Point, 78);
        graphics.DrawRectangle(new XSolidBrush(Gold), 0, 78, page.Width.Point, 4);
        graphics.DrawString("DOMINIO DE LA SIERRA", title, white, new XRect(48, 22, page.Width.Point - 96, 24), XStringFormats.TopLeft);
        graphics.DrawString(invoice.Fictional ? "Documento de prueba" : "Factura", small, new XSolidBrush(Gold), new XRect(48, 48, 240, 14), XStringFormats.TopLeft);
        var y = 98d;
        if (!invoice.Fictional)
        {
            return y;
        }

        graphics.DrawRectangle(new XSolidBrush(Cream), 48, y, page.Width.Point - 96, 28);
        graphics.DrawString(Invoice.FictionalBanner, small, new XSolidBrush(Wine), new XRect(56, y + 8, page.Width.Point - 112, 14), XStringFormats.TopLeft);
        return y + 40;
    }

    private static double DrawContinued(XGraphics graphics, PdfPage page, Invoice invoice, XFont heading, XBrush white)
    {
        graphics.DrawRectangle(new XSolidBrush(Wine), 0, 0, page.Width.Point, 42);
        graphics.DrawString("DOMINIO DE LA SIERRA", heading, white, new XRect(48, 14, 260, 16), XStringFormats.TopLeft);
        graphics.DrawString(invoice.FullNumber, heading, new XSolidBrush(Gold), new XRect(page.Width.Point - 220, 14, 172, 16), XStringFormats.TopRight);
        return 62;
    }

    private static double DrawParties(XGraphics graphics, Invoice invoice, XFont heading, XFont regular, XBrush ink, double y)
    {
        graphics.DrawString("Emisor", heading, new XSolidBrush(Wine), new XRect(48, y, 240, 16), XStringFormats.TopLeft);
        graphics.DrawString("Cliente", heading, new XSolidBrush(Wine), new XRect(320, y, 227, 16), XStringFormats.TopLeft);
        y += 18;
        var seller = new[]
        {
            invoice.SellerLegalName,
            invoice.SellerTaxId,
            invoice.SellerAddressLine,
            $"{invoice.SellerPostalCode} {invoice.SellerCity}",
            invoice.SellerProvince,
            invoice.SellerCountryCode
        };
        var buyer = new[]
        {
            invoice.BuyerName,
            invoice.BuyerTaxId,
            invoice.BuyerAddressLine,
            $"{invoice.BuyerPostalCode} {invoice.BuyerCity}",
            invoice.BuyerProvince,
            invoice.BuyerCountryCode
        };
        for (var index = 0; index < seller.Length; index++)
        {
            graphics.DrawString(Fit(graphics, seller[index], regular, 250), regular, ink, new XRect(48, y, 250, 14), XStringFormats.TopLeft);
            graphics.DrawString(Fit(graphics, buyer[index], regular, 227), regular, ink, new XRect(320, y, 227, 14), XStringFormats.TopLeft);
            y += 14;
        }

        return y + 12;
    }

    private static double DrawFacts(XGraphics graphics, Invoice invoice, XFont heading, XFont regular, XBrush ink, double y)
    {
        DrawFact(graphics, "Número", invoice.FullNumber, heading, regular, ink, 48, y);
        DrawFact(graphics, "Fecha de emisión", FormatIssuedAt(invoice.IssuedAt), heading, regular, ink, 220, y);
        DrawFact(graphics, "Pedido", invoice.OrderNumber, heading, regular, ink, 390, y);
        return y + 42;
    }

    private static void DrawFact(XGraphics graphics, string label, string value, XFont heading, XFont regular, XBrush ink, double x, double y)
    {
        graphics.DrawString(label, heading, new XSolidBrush(Muted), new XRect(x, y, 160, 12), XStringFormats.TopLeft);
        graphics.DrawString(Fit(graphics, value, regular, 160), regular, ink, new XRect(x, y + 14, 160, 14), XStringFormats.TopLeft);
    }

    private static double DrawTableHeader(XGraphics graphics, PdfPage page, XFont heading, XBrush white, double y)
    {
        graphics.DrawRectangle(new XSolidBrush(Wine), 48, y, page.Width.Point - 96, 20);
        graphics.DrawString("Concepto", heading, white, new XRect(54, y + 4, 210, 14), XStringFormats.TopLeft);
        graphics.DrawString("Cant.", heading, white, new XRect(270, y + 4, 40, 14), XStringFormats.TopRight);
        graphics.DrawString("Precio", heading, white, new XRect(316, y + 4, 70, 14), XStringFormats.TopRight);
        graphics.DrawString("IVA", heading, white, new XRect(392, y + 4, 50, 14), XStringFormats.TopRight);
        graphics.DrawString("Importe", heading, white, new XRect(448, y + 4, 90, 14), XStringFormats.TopRight);
        return y + 24;
    }

    private static double DrawLine(XGraphics graphics, InvoiceLine line, XFont regular, XBrush ink, double y)
    {
        var concept = Fit(graphics, $"{line.Reference}  {line.Name}", regular, 210);
        graphics.DrawString(concept, regular, ink, new XRect(54, y, 210, 14), XStringFormats.TopLeft);
        graphics.DrawString(line.Quantity.ToString(Spanish), regular, ink, new XRect(270, y, 40, 14), XStringFormats.TopRight);
        graphics.DrawString(Money(line.UnitPriceCents), regular, ink, new XRect(316, y, 70, 14), XStringFormats.TopRight);
        graphics.DrawString(Rate(line.VatRate), regular, ink, new XRect(392, y, 50, 14), XStringFormats.TopRight);
        graphics.DrawString(Money(line.LineTotalCents), regular, ink, new XRect(448, y, 90, 14), XStringFormats.TopRight);
        return y + 16;
    }

    private static double DrawShipping(XGraphics graphics, Invoice invoice, XFont regular, XBrush ink, double y)
    {
        graphics.DrawString("Transporte", regular, ink, new XRect(54, y, 210, 14), XStringFormats.TopLeft);
        graphics.DrawString(Rate(invoice.ShippingVatRate), regular, ink, new XRect(392, y, 50, 14), XStringFormats.TopRight);
        graphics.DrawString(Money(invoice.ShippingCents), regular, ink, new XRect(448, y, 90, 14), XStringFormats.TopRight);
        return y + 22;
    }

    private static void DrawSummary(XGraphics graphics, Invoice invoice, XFont heading, XFont regular, XFont small, XBrush ink, XBrush muted, double y)
    {
        graphics.DrawString("Base imponible", heading, new XSolidBrush(Wine), new XRect(300, y, 110, 14), XStringFormats.TopLeft);
        graphics.DrawString("IVA", heading, new XSolidBrush(Wine), new XRect(410, y, 50, 14), XStringFormats.TopRight);
        graphics.DrawString("Cuota", heading, new XSolidBrush(Wine), new XRect(466, y, 72, 14), XStringFormats.TopRight);
        y += 16;
        foreach (var group in Groups(invoice))
        {
            graphics.DrawString(Money(group.BaseCents), regular, ink, new XRect(300, y, 100, 14), XStringFormats.TopLeft);
            graphics.DrawString(Rate(group.Rate), regular, ink, new XRect(410, y, 50, 14), XStringFormats.TopRight);
            graphics.DrawString(Money(group.VatCents), regular, ink, new XRect(466, y, 72, 14), XStringFormats.TopRight);
            y += 14;
        }

        y += 8;
        graphics.DrawString("Total", heading, new XSolidBrush(Wine), new XRect(300, y, 80, 16), XStringFormats.TopLeft);
        graphics.DrawString(Money(invoice.TotalCents), heading, ink, new XRect(390, y, 148, 16), XStringFormats.TopRight);
        y += 22;
        graphics.DrawString("Importes en EUR. El precio incluye el IVA indicado.", small, muted, new XRect(48, y, 500, 12), XStringFormats.TopLeft);
    }

    private static IReadOnlyList<(decimal Rate, long BaseCents, long VatCents)> Groups(Invoice invoice)
    {
        var groups = invoice.Lines
            .GroupBy(line => line.VatRate)
            .Select(group => (Rate: group.Key, BaseCents: group.Sum(line => line.TaxableBaseCents), VatCents: group.Sum(line => line.VatCents)))
            .ToList();
        var shipping = groups.FindIndex(group => group.Rate == invoice.ShippingVatRate);
        if (shipping >= 0)
        {
            var current = groups[shipping];
            groups[shipping] = (current.Rate, current.BaseCents + invoice.ShippingTaxableBaseCents, current.VatCents + invoice.ShippingVatCents);
        }
        else
        {
            groups.Add((invoice.ShippingVatRate, invoice.ShippingTaxableBaseCents, invoice.ShippingVatCents));
        }

        return groups.OrderBy(group => group.Rate).ToList();
    }

    private static void DrawPageNumbers(PdfDocument document, XFont small, XBrush muted)
    {
        var total = document.PageCount;
        for (var index = 0; index < total; index++)
        {
            var page = document.Pages[index];
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            graphics.DrawString($"Página {index + 1} de {total}", small, muted, new XRect(48, page.Height.Point - 36, page.Width.Point - 96, 12), XStringFormats.TopRight);
        }
    }

    private static string FormatIssuedAt(DateTimeOffset issuedAt) =>
        AdminOrderClock.Convert(issuedAt).ToString("dd/MM/yyyy HH:mm", Spanish);

    public static string Money(long cents) => (cents / 100m).ToString("N2", Spanish) + " EUR";

    private static string Rate(decimal rate) => rate.ToString("N2", Spanish) + " %";

    private static string Fit(XGraphics graphics, string text, XFont font, double width)
    {
        if (graphics.MeasureString(text, font).Width <= width)
        {
            return text;
        }

        const string ellipsis = "...";
        var cut = text;
        while (cut.Length > 1 && graphics.MeasureString(cut + ellipsis, font).Width > width)
        {
            cut = cut[..^1];
        }

        return cut + ellipsis;
    }

    private static string ResolveFamily()
    {
        foreach (var name in new[] { "Calibri", "Segoe UI", "Arial" })
        {
            try
            {
                _ = new XFont(name, 10, XFontStyleEx.Regular);
                return name;
            }
            catch (Exception)
            {
                continue;
            }
        }

        throw new InvalidOperationException("No hay una fuente disponible para la factura.");
    }
}
