using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.UnitTests;

public class PayPalCheckoutTests
{
    private static readonly Guid OrderId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Formats_euro_cents_without_a_float_and_rejects_other_shapes()
    {
        Assert.Equal("18.90", PayPalProtocol.FormatEuros(1_890));
        Assert.Equal("0.05", PayPalProtocol.FormatEuros(5));
        Assert.Equal("100.00", PayPalProtocol.FormatEuros(10_000));
        Assert.True(PayPalProtocol.TryParseAmount("18.90", out var cents));
        Assert.Equal(1_890, cents);
        Assert.False(PayPalProtocol.TryParseAmount("18.9", out _));
        Assert.False(PayPalProtocol.TryParseAmount("18,90", out _));
        Assert.False(PayPalProtocol.TryParseAmount("-1.00", out _));
    }

    [Fact]
    public void Keeps_distinct_request_ids_inside_the_paypal_limit()
    {
        var paymentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var create = PayPalProtocol.CreateRequestId(paymentId);
        var capture = PayPalProtocol.CaptureRequestId(paymentId);

        Assert.NotEqual(create, capture);
        Assert.True(create.Length <= PayPalRules.RequestIdMaxLength);
        Assert.True(capture.Length <= PayPalRules.RequestIdMaxLength);
        Assert.Equal(create, PayPalProtocol.CreateRequestId(paymentId));
    }

    [Fact]
    public void Does_not_treat_a_repeated_post_as_a_lookup_inside_or_outside_the_documented_window()
    {
        var command = new PayPalCreateCommand(OrderId, "DS-1", 826, "Ana", "Calle 1", "37001", "Salamanca", "Salamanca", "http://localhost:4200/checkout/exito", "http://localhost:4200/checkout/cancelado");
        var changed = command with { AmountCents = 827 };
        var attempted = new DateTimeOffset(2026, 10, 20, 10, 30, 0, TimeSpan.Zero);

        Assert.Equal(PayPalProtocol.RequestFingerprint(command), PayPalProtocol.RequestFingerprint(command));
        Assert.NotEqual(PayPalProtocol.RequestFingerprint(command), PayPalProtocol.RequestFingerprint(changed));
        Assert.Equal(64, PayPalProtocol.RequestFingerprint(command).Length);
        Assert.True(PayPalCreateMarkers.WithinDocumentedWindow(attempted, attempted.AddHours(6)));
        Assert.False(PayPalCreateMarkers.WithinDocumentedWindow(attempted, attempted.AddHours(6).AddTicks(1)));
        Assert.False(PayPalProtocol.MayPostCreate(PayPalCreateBlock.InsideDocumentedWindow, externalOrderIdKnown: false));
        Assert.False(PayPalProtocol.MayPostCreate(PayPalCreateBlock.OutsideDocumentedWindow, externalOrderIdKnown: false));
        Assert.False(PayPalProtocol.MayPostCreate(PayPalCreateBlock.None, externalOrderIdKnown: true));
        Assert.True(PayPalProtocol.MayPostCreate(PayPalCreateBlock.None, externalOrderIdKnown: false));
    }

    [Fact]
    public void Remembers_the_paypal_order_before_the_approval_url_and_rejects_a_live_link()
    {
        var order = OrderEntity();
        var paymentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        order.AddPayment(paymentId, CreatedAt, PaymentProvider.PayPal);
        order.BeginPayPalCreate(paymentId, PayPalProtocol.CreateRequestId(paymentId), new string('a', 64), CreatedAt);
        order.RememberPayPalOrder(paymentId, "5O190127TN364715T");
        var payment = order.Payments.Single();

        Assert.Equal("5O190127TN364715T", payment.ExternalOperationId);
        Assert.Null(payment.CheckoutUrl);
        Assert.Equal(PayPalCreateOutcome.Identified, payment.ExternalCreateOutcome);
        Assert.Throws<ArgumentException>(() => order.AttachPayPalApproval(
            paymentId,
            "5O190127TN364715T",
            "https://www.paypal.com/checkoutnow?token=LIVE",
            CreatedAt.AddMinutes(30)));
        Assert.Null(payment.CheckoutUrl);
        order.AttachPayPalApproval(
            paymentId,
            "5O190127TN364715T",
            "https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T",
            CreatedAt.AddMinutes(30));
        Assert.StartsWith("https://www.sandbox.paypal.com/", payment.CheckoutUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CREATED", PaymentEvidence.StillPayable, false)]
    [InlineData("SAVED", PaymentEvidence.StillPayable, false)]
    [InlineData("APPROVED", PaymentEvidence.StillPayable, true)]
    [InlineData("PAYER_ACTION_REQUIRED", PaymentEvidence.StillPayable, false)]
    public void Does_not_treat_an_approved_order_as_paid(string status, PaymentEvidence evidence, bool canCapture)
    {
        var reading = PayPalProtocol.Interpret(Sample(status, []), OrderId, "DS-1", 1_890);

        Assert.Equal(evidence, reading.Evidence);
        Assert.Equal(canCapture, reading.CanCapture);
        Assert.Null(reading.CaptureId);
        Assert.NotEqual(PaymentEvidence.Paid, reading.Evidence);
    }

    [Fact]
    public void Pays_only_a_completed_capture_for_the_same_amount_and_currency()
    {
        var paid = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("COMPLETED", 1_890, "EUR")]), OrderId, "DS-1", 1_890);
        var declined = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("DECLINED", 1_890, "EUR")]), OrderId, "DS-1", 1_890);
        var wrongAmount = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("COMPLETED", 1, "EUR")]), OrderId, "DS-1", 1_890);
        var wrongCurrency = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("COMPLETED", 1_890, "USD")]), OrderId, "DS-1", 1_890);
        var pending = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("PENDING", 1_890, "EUR")]), OrderId, "DS-1", 1_890);
        var twice = PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("COMPLETED", 1_890, "EUR"), Capture("COMPLETED", 1_890, "EUR", "SECOND")]), OrderId, "DS-1", 1_890);
        var voided = PayPalProtocol.Interpret(Sample("VOIDED", []), OrderId, "DS-1", 1_890);
        var stranger = PayPalProtocol.Interpret(Sample("APPROVED", []) with { CustomId = "otro" }, OrderId, "DS-1", 1_890);

        Assert.Equal(PaymentEvidence.Paid, paid.Evidence);
        Assert.Equal("3C679366HH908993F", paid.CaptureId);
        Assert.False(paid.CanCapture);
        Assert.Equal(PaymentEvidence.ClosedUnpaid, declined.Evidence);
        Assert.Equal(PaymentEvidence.Unknown, wrongAmount.Evidence);
        Assert.Equal(PayPalRules.AmountMessage, wrongAmount.Attention);
        Assert.Equal(PayPalRules.CurrencyMessage, wrongCurrency.Attention);
        Assert.Equal(PayPalRules.PendingCaptureMessage, pending.Attention);
        Assert.Equal(PaymentEvidence.Unknown, twice.Evidence);
        Assert.Equal(PaymentEvidence.ClosedUnpaid, voided.Evidence);
        Assert.Equal(PayPalRules.IdentityMessage, stranger.Attention);
        Assert.False(PaymentRelease.MayReleaseStock(pending.Evidence));
        Assert.False(PaymentRelease.MayReleaseStock(PaymentEvidence.StillPayable));
        Assert.True(PaymentRelease.MayReleaseStock(declined.Evidence));
    }

    [Fact]
    public void Accepts_only_a_sandbox_approval_url_and_a_sandbox_certificate()
    {
        Assert.True(PayPalProtocol.IsSandboxApprovalUrl("https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T"));
        Assert.False(PayPalProtocol.IsSandboxApprovalUrl("https://www.paypal.com/checkoutnow?token=LIVE"));
        Assert.True(PayPalProtocol.IsSandboxPayerRedirect(Sample("PAYER_ACTION_REQUIRED", []), PayPalProtocol.Interpret(Sample("PAYER_ACTION_REQUIRED", []), OrderId, "DS-1", 1_890)));
        Assert.True(PayPalProtocol.IsSandboxPayerRedirect(Sample("CREATED", []), PayPalProtocol.Interpret(Sample("CREATED", []), OrderId, "DS-1", 1_890)));
        var liveLink = Sample("PAYER_ACTION_REQUIRED", []) with { ApproveUrl = "https://www.paypal.com/checkoutnow?token=LIVE" };
        Assert.False(PayPalProtocol.IsSandboxPayerRedirect(liveLink, PayPalProtocol.Interpret(liveLink, OrderId, "DS-1", 1_890)));
        Assert.False(PayPalProtocol.IsSandboxPayerRedirect(Sample("COMPLETED", [Capture("COMPLETED", 1_890, "EUR")]), PayPalProtocol.Interpret(Sample("COMPLETED", [Capture("COMPLETED", 1_890, "EUR")]), OrderId, "DS-1", 1_890)));
        Assert.False(PayPalProtocol.IsSandboxPayerRedirect(Sample("VOIDED", []), PayPalProtocol.Interpret(Sample("VOIDED", []), OrderId, "DS-1", 1_890)));
        Assert.False(PayPalProtocol.IsSandboxApprovalUrl("https://checkout.stripe.com/c/pay/cs_test_x"));
        Assert.True(PayPalProtocol.IsSandboxCertificateUrl("https://api.sandbox.paypal.com/v1/notifications/certs/CERT-1"));
        Assert.False(PayPalProtocol.IsSandboxCertificateUrl("https://api.paypal.com/v1/notifications/certs/CERT-1"));
        Assert.False(PayPalProtocol.IsSandboxCertificateUrl("https://api.sandbox.paypal.com/v1/notifications/certs/CERT-1?x=1"));
        Assert.True(PayPalProtocol.IsLiveApiHost("api-m.paypal.com"));
        Assert.False(PayPalProtocol.IsLiveApiHost("api-m.sandbox.paypal.com"));
        Assert.Equal(PayPalRules.SandboxApiRoot, "https://api-m.sandbox.paypal.com");
        Assert.False(PayPalProtocol.UseScript("Production", "Script"));
        Assert.True(PayPalProtocol.UseScript("Testing", "Script"));
    }

    [Fact]
    public void Reads_an_orders_v2_resource_without_payer_data()
    {
        const string json = """
            {
              "id": "5O190127TN364715T",
              "status": "COMPLETED",
              "purchase_units": [{
                "custom_id": "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
                "invoice_id": "DS-1",
                "amount": { "currency_code": "EUR", "value": "18.90" },
                "payments": { "captures": [{ "id": "3C679366HH908993F", "status": "COMPLETED", "amount": { "currency_code": "EUR", "value": "18.90" } }] }
              }],
              "links": [{ "href": "https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T", "rel": "payer-action", "method": "GET" }]
            }
            """;
        using var document = JsonDocument.Parse(json);
        var order = PayPalProtocol.ReadOrder(document.RootElement);
        var reading = PayPalProtocol.Interpret(order!, OrderId, "DS-1", 1_890);

        Assert.Equal(PaymentEvidence.Paid, reading.Evidence);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verifies_the_official_crc32_message_and_rejects_a_bad_signature()
    {
        var body = Encoding.UTF8.GetBytes("123456789");
        Assert.Equal(3_421_780_262u, PayPalProtocol.Crc32(body));
        using var rsa = RSA.Create(2048);
        var certificate = new CertificateRequest("CN=paypal-sandbox-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var pem = certificate.ExportCertificatePem();
        var message = PayPalProtocol.Message("tx-1", "2026-10-14T08:00:01Z", "8AB12345CD678901E", body);
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var transmission = new PayPalTransmission("tx-1", "2026-10-14T08:00:01Z", signature, "https://api.sandbox.paypal.com/v1/notifications/certs/CERT-1", PayPalRules.Algorithm);
        var broken = transmission with { Signature = Convert.ToBase64String(new byte[32]) };
        var liveCert = transmission with { CertificateUrl = "https://api.paypal.com/v1/notifications/certs/CERT-1" };

        Assert.True(PayPalProtocol.Verify(transmission, body, "8AB12345CD678901E", pem));
        Assert.False(PayPalProtocol.Verify(broken, body, "8AB12345CD678901E", pem));
        Assert.False(PayPalProtocol.Verify(liveCert, body, "8AB12345CD678901E", pem));
        Assert.False(PayPalProtocol.Verify(transmission, body, "otro-webhook", pem));
    }

    [Fact]
    public void Announces_paypal_only_for_a_complete_sandbox_configuration()
    {
        var catalog = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(Operational()).Build());
        Assert.Equal(["PayPal", "Stripe"], catalog.Configured().Select(offer => offer.Code).OrderBy(code => code).ToArray());
        catalog.EnsureSelectable("PayPal");

        var live = Operational();
        live["PayPal:Environment"] = "Live";
        Assert.DoesNotContain(
            new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(live).Build()).Configured(),
            offer => offer.Code == "PayPal");

        var missingWebhook = Operational();
        missingWebhook["PayPal:WebhookId"] = "";
        Assert.Throws<InvalidOperationException>(() =>
            new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(missingWebhook).Build()).EnsureSelectable("PayPal"));
    }

    [Fact]
    public void Stores_one_capture_id_and_refuses_a_second()
    {
        var order = OrderEntity();
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt, PaymentProvider.PayPal);
        order.AssignExternalCheckout(payment.Id, "5O190127TN364715T", "https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T", CreatedAt.AddMinutes(30), null);
        order.AssignCapture(payment.Id, "3C679366HH908993F");
        order.AssignCapture(payment.Id, "3C679366HH908993F");

        var error = Assert.Throws<InvalidOperationException>(() => order.AssignCapture(payment.Id, "DIFFERENTCAPTURE1"));
        Assert.Equal("3C679366HH908993F", payment.ExternalCaptureId);
        Assert.Equal(PaymentProviderRules.ExternalAssignedMessage, error.Message);
        Assert.Equal(OrderAmounts.ReservationDuration, TimeSpan.FromMinutes(30));
        Assert.NotEqual(StripeCheckoutExpiry.PaymentHold, OrderAmounts.ReservationDuration);
    }

    private static PayPalOrderSnapshot Sample(string status, IReadOnlyList<PayPalCaptureView> captures)
    {
        return new PayPalOrderSnapshot(
            "5O190127TN364715T",
            status,
            PayPalProtocol.CustomId(OrderId),
            "DS-1",
            1_890,
            "EUR",
            "https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T",
            captures,
            1);
    }

    private static PayPalCaptureView Capture(string status, long cents, string currency, string id = "3C679366HH908993F")
    {
        return new PayPalCaptureView(id, status, cents, currency);
    }

    [Fact]
    public void Keeps_a_historical_paypal_create_when_the_recovery_columns_are_null()
    {
        var order = OrderEntity();
        var payment = order.AddPayment(Guid.NewGuid(), DateTimeOffset.UnixEpoch, PaymentProvider.PayPal);
        var recorded = order.RecordPaymentEvent(
            payment.Id,
            Guid.NewGuid(),
            PayPalCreateMarkers.EventId(payment.Id),
            "paypal.order.create",
            DateTimeOffset.UnixEpoch);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, DateTimeOffset.UnixEpoch, null);

        Assert.Null(payment.ExternalCreateOutcome);
        Assert.Null(payment.ExternalOperationId);
        Assert.True(payment.BlocksAutomaticRelease());
        var error = Assert.Throws<InvalidOperationException>(() => order.Cancel(order.ReservationExpiresAt));
        Assert.Equal(ExternalHoldRules.UncertainHoldMessage, error.Message);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void Keeps_a_pending_capture_and_still_expires_a_plain_stripe_hold()
    {
        var paypal = OrderEntity();
        var payment = paypal.AddPayment(Guid.NewGuid(), DateTimeOffset.UnixEpoch, PaymentProvider.PayPal);
        paypal.AssignCapture(payment.Id, "3C679366HH908993F");
        Assert.True(payment.BlocksAutomaticRelease());
        Assert.Throws<InvalidOperationException>(() => paypal.Expire(paypal.ReservationExpiresAt));
        Assert.Equal(OrderStatus.PendingPayment, paypal.Status);

        var stripe = OrderEntity();
        stripe.AddPayment(Guid.NewGuid(), DateTimeOffset.UnixEpoch, PaymentProvider.Stripe);
        stripe.Expire(stripe.ReservationExpiresAt);
        Assert.Equal(OrderStatus.Expired, stripe.Status);
    }

    private static Dictionary<string, string?> Operational()
    {
        return new Dictionary<string, string?>
        {
            ["Stripe:Gateway"] = "Stub",
            ["PayPal:Enabled"] = "true",
            ["PayPal:Environment"] = "Sandbox",
            ["PayPal:ClientId"] = "sandbox-client-id",
            ["PayPal:ClientSecret"] = "clave-paypal-local",
            ["PayPal:WebhookId"] = "8AB12345CD678901E",
            ["PayPal:ReturnUrl"] = "http://localhost:4200/checkout/exito",
            ["PayPal:CancelUrl"] = "http://localhost:4200/checkout/cancelado"
        };
    }

    private static Order OrderEntity()
    {
        return Order.CreatePending(
            OrderId,
            "DS-1",
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, 1_890, 21m, null)],
            CreatedAt);
    }
}
