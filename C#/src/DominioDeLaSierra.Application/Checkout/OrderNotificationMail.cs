using System.Globalization;
using System.Net;
using System.Text;

namespace DominioDeLaSierra.Application.Checkout;

public sealed record PaidConfirmationLine(
    string Name,
    string Reference,
    int Quantity,
    long UnitPriceCents,
    long LineTotalCents);

public sealed record PaidConfirmationContent(
    string Number,
    string CustomerName,
    string Email,
    IReadOnlyList<PaidConfirmationLine> Lines,
    long ProductSubtotalCents,
    long ProductVatCents,
    long ShippingCents,
    long? ShippingVatCents,
    long TotalCents,
    DateTimeOffset OrderedAt,
    string PaymentMethod,
    string AddressLine,
    string PostalCode,
    string City,
    string Province,
    string CountryLabel,
    string? DeliveryNotes);

public sealed record OrderMail(
    string Recipient,
    string Subject,
    string TextBody,
    string HtmlBody,
    string MessageId);

public static class PaidConfirmationMail
{
    public const string ShippedLaterMessage = "Cuando el pedido salga, te escribiremos con el transportista y el seguimiento.";
    public const string PreparingMessage = "Estamos preparando tu pedido. No hay un plazo de salida fijado.";
    public const string StoreEmail = "info@dominiodelasierra.com";
    public const string StorePhone = "+34 665 144 697";
    public const string StorePlace = "San Esteban de la Sierra, Salamanca";
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static OrderMail Compose(PaidConfirmationContent content, string messageId)
    {
        ArgumentNullException.ThrowIfNull(content);
        var number = Line(content.Number, "El número de pedido");
        var name = Line(content.CustomerName, "El nombre");
        var email = Line(content.Email, "El correo electrónico");
        if (email.Count(character => character == '@') != 1 || email.Contains(' '))
        {
            throw new ArgumentException("El correo electrónico no es válido.");
        }

        var message = Line(messageId, "El identificador del mensaje");
        if (content.Lines is null || content.Lines.Count == 0)
        {
            throw new ArgumentException("El aviso de confirmación no tiene productos.");
        }

        var address = Line(content.AddressLine, "La dirección");
        var postalCode = Line(content.PostalCode, "El código postal");
        var city = Line(content.City, "La localidad");
        var province = Line(content.Province, "La provincia");
        var country = Line(content.CountryLabel, "El país");
        var notes = string.IsNullOrWhiteSpace(content.DeliveryNotes) ? null : Line(content.DeliveryNotes, "Las indicaciones");
        var products = Euros(content.ProductSubtotalCents);
        var productVat = Euros(content.ProductVatCents);
        var shipping = Euros(content.ShippingCents);
        var shippingVat = content.ShippingVatCents is long shippingVatCents ? Euros(shippingVatCents) : null;
        var total = Euros(content.TotalCents);
        var orderedAt = FormatOrderedAt(content.OrderedAt);
        var paymentMethod = Line(content.PaymentMethod, "El método de pago");
        var lines = content.Lines.Select(line => new PreparedLine(
            Line(line.Name, "El producto"),
            Line(line.Reference, "La referencia"),
            line.Quantity,
            Euros(line.UnitPriceCents),
            Euros(line.LineTotalCents))).ToArray();
        if (lines.Any(line => line.Quantity <= 0))
        {
            throw new ArgumentException("La cantidad del aviso no es válida.");
        }

        var subject = $"Hemos recibido tu pedido {number} | Dominio de la Sierra";
        return new OrderMail(
            email,
            subject,
            Text(name, number, orderedAt, paymentMethod, lines, products, productVat, shipping, shippingVat, total, address, postalCode, city, province, country, notes),
            Html(subject, name, number, orderedAt, paymentMethod, lines, products, productVat, shipping, shippingVat, total, address, postalCode, city, province, country, notes),
            message);
    }

    public static string Euros(long cents)
    {
        if (cents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cents), "El importe del aviso no es válido.");
        }

        return string.Create(Spanish, $"{cents / 100m:N2} €");
    }

    private static string Text(
        string name,
        string number,
        string orderedAt,
        string paymentMethod,
        IReadOnlyList<PreparedLine> lines,
        string products,
        string productVat,
        string shipping,
        string? shippingVat,
        string total,
        string address,
        string postalCode,
        string city,
        string province,
        string country,
        string? notes)
    {
        var body = new StringBuilder();
        body.AppendLine("Dominio de la Sierra");
        body.AppendLine();
        body.Append("Hola ").Append(name).AppendLine(",");
        body.AppendLine();
        body.AppendLine("Hemos recibido tu pedido y confirmado el pago.");
        body.AppendLine("Gracias por confiar en nuestra bodega.");
        body.AppendLine();
        body.Append("Número de pedido: ").AppendLine(number);
        body.Append("Fecha del pedido: ").AppendLine(orderedAt);
        body.AppendLine("Estado: Pago confirmado");
        body.Append("Método de pago: ").AppendLine(paymentMethod);
        body.AppendLine();
        foreach (var line in lines)
        {
            body.Append(line.Name)
                .Append(" (").Append(line.Reference).Append(") · ")
                .Append(line.Quantity).Append(" × ")
                .Append(line.UnitPrice).Append(" · ")
                .AppendLine(line.LineTotal);
        }

        body.AppendLine();
        body.Append("Productos, IVA incluido: ").AppendLine(products);
        body.Append("Envío, IVA incluido: ").AppendLine(shipping);
        body.Append("IVA incluido en los productos: ").AppendLine(productVat);
        if (shippingVat is not null)
        {
            body.Append("IVA incluido en el envío: ").AppendLine(shippingVat);
        }

        body.Append("Total pagado: ").AppendLine(total);
        body.AppendLine("El IVA ya va incluido en los importes. No se suma otra vez.");
        body.AppendLine();
        body.AppendLine("Entrega:");
        body.AppendLine(address);
        body.Append(postalCode).Append(' ').AppendLine(city);
        body.AppendLine(province);
        body.AppendLine(country);
        if (notes is not null)
        {
            body.AppendLine(notes);
        }

        body.AppendLine();
        body.AppendLine(PreparingMessage);
        body.AppendLine(ShippedLaterMessage);
        body.AppendLine();
        body.AppendLine(StorePlace);
        body.AppendLine(StoreEmail);
        body.Append("WhatsApp ").AppendLine(StorePhone);
        return body.ToString();
    }

    private static string Html(
        string subject,
        string name,
        string number,
        string orderedAt,
        string paymentMethod,
        IReadOnlyList<PreparedLine> lines,
        string products,
        string productVat,
        string shipping,
        string? shippingVat,
        string total,
        string address,
        string postalCode,
        string city,
        string province,
        string country,
        string? notes)
    {
        var rows = new StringBuilder();
        foreach (var line in lines)
        {
            rows.Append("<tr><td style=\"padding:12px 0;border-bottom:1px solid #d9d3cb;color:#292628;\">")
                .Append(Encode(line.Name))
                .Append("<div style=\"color:#625d5b;font-size:13px;\">").Append(Encode(line.Reference)).Append("</div></td>")
                .Append("<td style=\"padding:12px 8px;border-bottom:1px solid #d9d3cb;text-align:right;color:#292628;\">")
                .Append(line.Quantity).Append("</td>")
                .Append("<td style=\"padding:12px 8px;border-bottom:1px solid #d9d3cb;text-align:right;color:#292628;\">")
                .Append(Encode(line.UnitPrice)).Append("</td>")
                .Append("<td style=\"padding:12px 0;border-bottom:1px solid #d9d3cb;text-align:right;color:#292628;\">")
                .Append(Encode(line.LineTotal)).Append("</td></tr>");
        }

        var shippingVatHtml = shippingVat is null
            ? string.Empty
            : "<p style=\"margin:4px 0;font-size:15px;\">IVA incluido en el envío: " + Encode(shippingVat) + "</p>";
        var notesHtml = notes is null
            ? string.Empty
            : "<p style=\"margin:8px 0 0;color:#625d5b;\">" + Encode(notes) + "</p>";
        return $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Encode(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background:#eeeae3;">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#eeeae3;margin:0;padding:24px 12px;">
            <tr><td align="center">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#faf9f6;border:1px solid #d9d3cb;">
            <tr><td style="background:#382029;padding:28px 32px;">
            <p style="margin:0 0 8px;font-family:Georgia,serif;font-size:12px;letter-spacing:.16em;color:#b69a71;">BODEGA</p>
            <p style="margin:0;font-family:Georgia,serif;font-size:28px;line-height:1.2;color:#faf9f6;">Dominio de la Sierra</p>
            </td></tr>
            <tr><td style="padding:32px;font-family:Georgia,serif;color:#292628;">
            <p style="margin:0 0 8px;font-size:13px;letter-spacing:.14em;color:#783b4b;">PAGO CONFIRMADO</p>
            <h1 style="margin:0 0 16px;font-size:28px;line-height:1.25;font-weight:normal;">Hola {Encode(name)}</h1>
            <p style="margin:0 0 8px;font-size:16px;line-height:1.7;">Hemos recibido tu pedido y confirmado el pago.</p>
            <p style="margin:0 0 24px;font-size:16px;line-height:1.7;">Gracias por confiar en nuestra bodega.</p>
            <p style="margin:0;font-size:15px;line-height:1.7;">Número de pedido: {Encode(number)}<br>Fecha del pedido: {Encode(orderedAt)}<br>Estado: Pago confirmado<br>Método de pago: {Encode(paymentMethod)}</p>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:24px;font-size:15px;">
            <tr><td style="padding-bottom:8px;color:#625d5b;font-size:12px;letter-spacing:.08em;">PRODUCTO</td><td style="padding-bottom:8px;color:#625d5b;font-size:12px;letter-spacing:.08em;text-align:right;">CANT.</td><td style="padding-bottom:8px;color:#625d5b;font-size:12px;letter-spacing:.08em;text-align:right;">PRECIO</td><td style="padding-bottom:8px;color:#625d5b;font-size:12px;letter-spacing:.08em;text-align:right;">SUBTOTAL</td></tr>
            {rows}
            </table>
            <p style="margin:20px 0 4px;font-size:15px;">Productos, IVA incluido: {Encode(products)}</p>
            <p style="margin:4px 0;font-size:15px;">Envío, IVA incluido: {Encode(shipping)}</p>
            <p style="margin:4px 0;font-size:15px;">IVA incluido en los productos: {Encode(productVat)}</p>
            {shippingVatHtml}
            <p style="margin:4px 0 0;font-size:14px;line-height:1.6;color:#625d5b;">El IVA ya va incluido en los importes. No se suma otra vez.</p>
            <p style="margin:12px 0 24px;font-size:18px;">Total pagado: {Encode(total)}</p>
            <p style="margin:0;font-size:12px;letter-spacing:.12em;color:#783b4b;">ENTREGA</p>
            <p style="margin:8px 0 0;font-size:16px;line-height:1.6;">{Encode(address)}<br>{Encode(postalCode)} {Encode(city)}<br>{Encode(province)}<br>{Encode(country)}</p>
            {notesHtml}
            <p style="margin:28px 0 0;padding-top:20px;border-top:1px solid #d9d3cb;font-size:15px;line-height:1.7;color:#625d5b;">{Encode(PreparingMessage)} {Encode(ShippedLaterMessage)}</p>
            </td></tr>
            <tr><td style="padding:20px 32px 28px;background:#eeeae3;font-family:Georgia,serif;font-size:13px;line-height:1.6;color:#625d5b;">
            Dominio de la Sierra<br>{Encode(StorePlace)}<br>{Encode(StoreEmail)}<br>WhatsApp {Encode(StorePhone)}
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    private static string FormatOrderedAt(DateTimeOffset orderedAt)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        var local = TimeZoneInfo.ConvertTime(orderedAt, zone);
        return local.ToString("d 'de' MMMM 'de' yyyy, HH:mm", Spanish);
    }

    private static string Line(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} es obligatorio.");
        }

        var compact = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = compact.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                compact.Append(' ');
                pendingSpace = false;
            }

            compact.Append(character);
        }

        var text = compact.ToString().Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException($"{label} es obligatorio.");
        }

        return text;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private sealed record PreparedLine(string Name, string Reference, int Quantity, string UnitPrice, string LineTotal);
}
