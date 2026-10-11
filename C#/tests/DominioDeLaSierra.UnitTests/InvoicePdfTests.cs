using System.Text;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Invoicing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DominioDeLaSierra.UnitTests;

public class InvoicePdfTests
{
    [Fact]
    public void Renders_a_multipage_fictional_document_with_spanish_text()
    {
        var invoice = Sample(40, fictional: true);
        var pdf = InvoicePdf.Render(invoice);

        Assert.Equal("ficticio-F2026-000001.pdf", invoice.DownloadFileName);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf[..5]));
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 2);
        var text = InvoicePdfInspection.Read(pdf);
        Assert.Contains(Invoice.FictionalBanner, text);
        Assert.Contains("F2026-000001", text);
        Assert.Contains("Emisor ficticio de pruebas", text);
        Assert.Contains("FICTICIO-TEST", text);
        Assert.Contains("Ávila", text);
        Assert.Contains("Señorío", text);
        Assert.Contains(InvoicePdf.Money(invoice.TotalCents), text);
        Assert.Contains("Página 1 de", text);
        Assert.Contains($"Página {document.PageCount} de {document.PageCount}", text);
    }

    [Fact]
    public void A_non_fictional_render_does_not_use_the_test_banner()
    {
        var invoice = Sample(1, fictional: false);
        var text = InvoicePdfInspection.Read(InvoicePdf.Render(invoice));

        Assert.Equal("factura-F2026-000001.pdf", invoice.DownloadFileName);
        Assert.DoesNotContain(Invoice.FictionalBanner, text);
        Assert.Contains("F2026-000001", text);
    }

    [Fact]
    public void Rejects_an_inconsistent_snapshot()
    {
        var line = Line("Vino", 100);
        var seller = Party("Emisor ficticio de pruebas");
        var buyer = Party("Señorío");

        Assert.Throws<ArgumentException>(() => Invoice.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DS-TEST-00000001",
            "F2026",
            1,
            DateTimeOffset.UnixEpoch,
            true,
            seller,
            buyer,
            [line],
            line.LineTotalCents + 1,
            line.TaxableBaseCents,
            line.VatCents,
            0,
            21m,
            0,
            0,
            line.LineTotalCents));
    }

    [Fact]
    public void Empty_seller_and_disabled_issuance_are_the_safe_defaults()
    {
        var options = new InvoiceOptions();

        Assert.False(options.Enabled);
        Assert.False(options.Fictional);
        Assert.False(options.IsSellerReady());
        InvoiceIssuanceGuard.Ensure("Development", options);
        InvoiceIssuanceGuard.Ensure("Testing", options);
        Assert.Throws<InvalidOperationException>(() => InvoiceIssuanceGuard.Ensure("Development", new InvoiceOptions { Fictional = true }));
        Assert.Throws<InvalidOperationException>(() => InvoiceIssuanceGuard.Ensure("Testing", new InvoiceOptions { Enabled = true }));
    }

    private static Invoice Sample(int lineCount, bool fictional)
    {
        var lines = new List<InvoiceLineDraft>();
        long subtotal = 0;
        long taxableBase = 0;
        long vat = 0;
        for (var index = 0; index < lineCount; index++)
        {
            var line = Line($"Vino {index + 1}", 100);
            lines.Add(line);
            subtotal += line.LineTotalCents;
            taxableBase += line.TaxableBaseCents;
            vat += line.VatCents;
        }

        return Invoice.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DS-TEST-00000001",
            "F2026",
            1,
            new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero),
            fictional,
            Party("Emisor ficticio de pruebas"),
            Party("Señorío de Ávila"),
            lines,
            subtotal,
            taxableBase,
            vat,
            0,
            21m,
            0,
            0,
            subtotal);
    }

    private static InvoiceLineDraft Line(string name, long cents)
    {
        var (taxableBase, vat) = OrderAmounts.SplitGross(cents, 21m);
        return new InvoiceLineDraft(name, "REF-01", 1, cents, 21m, cents, taxableBase, vat);
    }

    private static InvoiceParty Party(string name) =>
        new(name, "FICTICIO-TEST", "Calle Ficticia 1", "05001", "Ávila", "Ávila", "ES");
}

internal static class InvoicePdfInspection
{
    public static string Read(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var content = new StringBuilder();
        foreach (PdfPage page in document.Pages)
        {
            content.Append(Encoding.Latin1.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue));
        }

        return Decode(content.ToString());
    }

    private static string Decode(string content)
    {
        var text = new StringBuilder();
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '(')
            {
                index++;
                while (index < content.Length && content[index] != ')')
                {
                    if (content[index] == '\\' && index + 1 < content.Length)
                    {
                        index++;
                    }

                    text.Append(content[index]);
                    index++;
                }
            }
            else if (content[index] == '<')
            {
                var end = content.IndexOf('>', index + 1);
                if (end <= index)
                {
                    break;
                }

                var hex = content[(index + 1)..end];
                if (hex.Length >= 4 && hex.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'))
                {
                    var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex[..^1]);
                    if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    {
                        text.Append(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
                    }
                    else
                    {
                        text.Append(Encoding.BigEndianUnicode.GetString(bytes));
                    }
                }

                index = end;
            }
        }

        return text.ToString();
    }
}
