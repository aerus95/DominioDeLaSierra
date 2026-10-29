using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.UnitTests;

public class PaymentProviderTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Keeps_a_stripe_test_session_and_copies_its_reference()
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.AssignHostedCheckout(
            payment.Id,
            "cs_test_provider",
            "https://checkout.stripe.com/c/pay/cs_test_provider",
            CreatedAt.AddMinutes(32));

        Assert.Equal(PaymentProvider.Stripe, payment.Provider);
        Assert.Equal(order.TotalCents, payment.AmountCents);
        Assert.Equal("EUR", payment.Currency);
        Assert.Equal("cs_test_provider", payment.StripeCheckoutSessionId);
        Assert.Equal("cs_test_provider", payment.ExternalOperationId);
        Assert.Equal("cs_test_provider", payment.ReconciliationReference());
        Assert.Null(payment.RedsysOrderNumber);
    }

    [Fact]
    public void Still_refuses_a_live_stripe_session()
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);

        var error = Assert.Throws<ArgumentException>(() => order.AssignHostedCheckout(
            payment.Id,
            "cs_live_provider",
            "https://checkout.stripe.com/c/pay/cs_live_provider",
            CreatedAt.AddMinutes(32)));

        Assert.Equal("La sesión de pago no es de pruebas.", error.Message);
    }

    [Fact]
    public void Accepts_a_redsys_operation_without_reusing_the_order_number()
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt, PaymentProvider.Redsys);
        order.AssignExternalCheckout(
            payment.Id,
            "redsys-op-1",
            "https://sis-t.redsys.es/sis/realizarPago",
            CreatedAt.AddMinutes(12),
            "100000000001");

        Assert.Equal(PaymentProvider.Redsys, payment.Provider);
        Assert.Equal("redsys-op-1", payment.ReconciliationReference());
        Assert.Equal("100000000001", payment.RedsysOrderNumber);
        Assert.Null(payment.StripeCheckoutSessionId);
        Assert.NotEqual(order.Number, payment.RedsysOrderNumber);
    }

    [Theory]
    [InlineData("DS-20261013-abc")]
    [InlineData("F2026-000001")]
    [InlineData("26")]
    [InlineData("1234567890123")]
    public void Rejects_a_redsys_number_outside_its_own_range(string number)
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt, PaymentProvider.Redsys);

        var error = Assert.Throws<ArgumentException>(() => order.AssignExternalCheckout(
            payment.Id,
            "redsys-op-2",
            "https://sis-t.redsys.es/sis/realizarPago",
            CreatedAt.AddMinutes(12),
            number));

        Assert.Equal(RedsysOperationNumber.InvalidMessage, error.Message);
    }

    [Fact]
    public void Rejects_card_data_and_stripe_identifiers_on_another_provider()
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt, PaymentProvider.PayPal);
        var expires = CreatedAt.AddMinutes(12);

        var card = Assert.Throws<ArgumentException>(() => order.AssignExternalCheckout(
            payment.Id,
            "4111111111111111",
            "https://www.sandbox.paypal.com/checkoutnow",
            expires,
            null));
        var stripeReference = Assert.Throws<ArgumentException>(() => order.AssignExternalCheckout(
            payment.Id,
            "cs_test_other",
            "https://www.sandbox.paypal.com/checkoutnow",
            expires,
            null));
        var stripeHost = Assert.Throws<ArgumentException>(() => order.AssignExternalCheckout(
            payment.Id,
            "paypal-order-1",
            "https://checkout.stripe.com/c/pay/cs_test_other",
            expires,
            null));

        Assert.Equal(PaymentProviderRules.CardMessage, card.Message);
        Assert.Equal(PaymentProviderRules.ExternalMismatchMessage, stripeReference.Message);
        Assert.Equal("La dirección de pago no es válida.", stripeHost.Message);
    }

    [Fact]
    public void Rejects_a_repeated_external_reference_on_the_same_payment()
    {
        var order = Place(1_890);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt, PaymentProvider.Redsys);
        order.AssignExternalCheckout(
            payment.Id,
            "redsys-op-3",
            "https://sis-t.redsys.es/sis/realizarPago",
            CreatedAt.AddMinutes(12),
            "100000000002");

        var error = Assert.Throws<InvalidOperationException>(() => order.AssignExternalCheckout(
            payment.Id,
            "redsys-op-4",
            "https://sis-t.redsys.es/sis/realizarPago",
            CreatedAt.AddMinutes(12),
            "100000000003"));

        Assert.Equal(PaymentProviderRules.ExternalAssignedMessage, error.Message);
    }

    [Fact]
    public void Does_not_reuse_the_stripe_hold_for_other_providers()
    {
        Assert.Equal(StripeCheckoutExpiry.PaymentHold, PaymentProviderHolds.ReservationHold(PaymentProvider.Stripe));
        var error = Assert.Throws<InvalidOperationException>(() => PaymentProviderHolds.ReservationHold(PaymentProvider.Redsys));
        Assert.Equal(PaymentProviderRules.HoldMessage, error.Message);
        Assert.Throws<InvalidOperationException>(() => PaymentProviderHolds.ReservationHold(PaymentProvider.PayPal));
    }

    [Theory]
    [InlineData(PaymentEvidence.Unknown, false)]
    [InlineData(PaymentEvidence.StillPayable, false)]
    [InlineData(PaymentEvidence.Paid, false)]
    [InlineData(PaymentEvidence.ClosedUnpaid, true)]
    public void Releases_stock_only_when_the_payment_can_no_longer_complete(PaymentEvidence evidence, bool mayRelease)
    {
        Assert.Equal(mayRelease, PaymentRelease.MayReleaseStock(evidence));
        Assert.False(PaymentConfirmation.BrowserReturnConfirms);
        Assert.False(PaymentRefundPolicy.IsImplemented(PaymentProvider.Stripe));
        Assert.False(PaymentRefundPolicy.IsImplemented(PaymentProvider.Redsys));
        Assert.False(PaymentRefundPolicy.IsImplemented(PaymentProvider.PayPal));
    }

    [Fact]
    public void Formats_the_redsys_sequence_without_touching_order_or_invoice_numbers()
    {
        Assert.Equal("100000000001", RedsysOperationNumber.FromSequence(RedsysOperationNumber.SequenceStart));
        Assert.Equal("1000", RedsysOperationNumber.FromSequence(1_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => RedsysOperationNumber.FromSequence(999));
        Assert.Throws<ArgumentException>(() => RedsysOperationNumber.Require("DS-20261013-abc"));
        Assert.Throws<ArgumentException>(() => RedsysOperationNumber.Require("F2026"));
    }

    [Fact]
    public void Hides_providers_that_cannot_start_even_when_credentials_are_present()
    {
        var catalog = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:Gateway"] = "Stub",
            ["Redsys:Enabled"] = "true",
            ["Redsys:MerchantCode"] = "999008881",
            ["Redsys:Terminal"] = "1",
            ["Redsys:SigningKey"] = "not-a-real-secret",
            ["PayPal:Enabled"] = "true",
            ["PayPal:ClientId"] = "sandbox-client",
            ["PayPal:ClientSecret"] = "not-a-real-secret"
        }).Build());

        var offer = Assert.Single(catalog.Configured());
        Assert.Equal(nameof(PaymentProvider.Stripe), offer.Code);
        Assert.Equal(PaymentProviderRules.RequiredMessage, Assert.Throws<ArgumentException>(() => catalog.EnsureSelectable(null)).Message);
        Assert.Equal(PaymentProviderRules.RequiredMessage, Assert.Throws<ArgumentException>(() => catalog.EnsureSelectable("  ")).Message);
        catalog.EnsureSelectable("Stripe");
        Assert.Throws<InvalidOperationException>(() => catalog.EnsureSelectable("Redsys"));
        Assert.Throws<InvalidOperationException>(() => catalog.EnsureSelectable("PayPal"));
        Assert.Throws<ArgumentException>(() => catalog.EnsureSelectable("Bizum"));
    }

    [Fact]
    public void Requires_the_provider_before_any_default_and_hides_an_empty_catalog()
    {
        var empty = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection().Build());
        Assert.Empty(empty.Configured());
        Assert.Equal(PaymentProviderRules.RequiredMessage, Assert.Throws<ArgumentException>(() => empty.EnsureSelectable(null)).Message);
        Assert.Equal(PaymentProviderRules.UnavailableMessage, Assert.Throws<InvalidOperationException>(() => empty.EnsureSelectable("Stripe")).Message);
        Assert.Equal(PaymentProviderRules.UnknownMessage, Assert.Throws<ArgumentException>(() => empty.EnsureSelectable("Bizum")).Message);

        var paypal = new Dictionary<string, string?>
        {
            ["PayPal:Enabled"] = "true",
            ["PayPal:Environment"] = "Sandbox",
            ["PayPal:ClientId"] = "sandbox-client-id",
            ["PayPal:ClientSecret"] = "clave-paypal-local",
            ["PayPal:WebhookId"] = "8AB12345CD678901E",
            ["PayPal:ReturnUrl"] = "http://localhost:4200/checkout/exito",
            ["PayPal:CancelUrl"] = "http://localhost:4200/checkout/cancelado"
        };
        var catalog = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(paypal).Build());
        var offer = Assert.Single(catalog.Configured());
        Assert.Equal(nameof(PaymentProvider.PayPal), offer.Code);
        catalog.EnsureSelectable("PayPal");
        Assert.Equal(PaymentProviderRules.UnavailableMessage, Assert.Throws<InvalidOperationException>(() => catalog.EnsureSelectable("Stripe")).Message);

        paypal["PayPal:Environment"] = "Live";
        Assert.DoesNotContain(
            new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(paypal).Build()).Configured(),
            offer => offer.Code == "PayPal");
        paypal["PayPal:Environment"] = "Sandbox";
        paypal["PayPal:ReturnUrl"] = "https://srv2027085.hstgr.cloud/checkout/exito";
        paypal["PayPal:CancelUrl"] = "https://srv2027085.hstgr.cloud/checkout/cancelado";
        Assert.Contains(
            new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(paypal).Build()).Configured(),
            offer => offer.Code == "PayPal");
        paypal["PayPal:ReturnUrl"] = "https://www.paypal.com/checkout/exito";
        Assert.DoesNotContain(
            new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(paypal).Build()).Configured(),
            offer => offer.Code == "PayPal");
    }

    [Fact]
    public void Announces_redsys_only_for_a_complete_test_terminal()
    {
        var catalog = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(OperationalRedsys()).Build());
        Assert.Equal(["Stripe", "Redsys"], catalog.Configured().Select(offer => offer.Code).ToArray());
        Assert.Equal("Tarjeta bancaria (Redsys)", catalog.Configured().Single(offer => offer.Code == "Redsys").DisplayName);
        catalog.EnsureSelectable("Redsys");

        var live = OperationalRedsys();
        live["Redsys:Environment"] = "Live";
        Assert.DoesNotContain(new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(live).Build()).Configured(), offer => offer.Code == "Redsys");

        var publicTest = OperationalRedsys();
        publicTest["Redsys:MerchantUrl"] = "https://srv2027085.hstgr.cloud/api/v1/checkout/redsys/notification";
        publicTest["Redsys:UrlOk"] = "https://srv2027085.hstgr.cloud/checkout/exito";
        publicTest["Redsys:UrlKo"] = "https://srv2027085.hstgr.cloud/checkout/cancelado";
        Assert.Contains(new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(publicTest).Build()).Configured(), offer => offer.Code == "Redsys");

        var clearHttp = OperationalRedsys();
        clearHttp["Redsys:MerchantUrl"] = "http://srv2027085.hstgr.cloud/api/v1/checkout/redsys/notification";
        Assert.DoesNotContain(new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(clearHttp).Build()).Configured(), offer => offer.Code == "Redsys");

        var legacy = OperationalRedsys();
        legacy["Redsys:SignatureVersion"] = "HMAC_SHA256_V1";
        Assert.Throws<InvalidOperationException>(() => new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(legacy).Build()).EnsureSelectable("Redsys"));
    }

    private static Dictionary<string, string?> OperationalRedsys()
    {
        return new Dictionary<string, string?>
        {
            ["Stripe:Gateway"] = "Stub",
            ["Redsys:Enabled"] = "true",
            ["Redsys:Environment"] = "Test",
            ["Redsys:MerchantCode"] = "999008881",
            ["Redsys:Terminal"] = "001",
            ["Redsys:SignatureVersion"] = RedsysSignature.Version,
            ["Redsys:SecretKey"] = "clave-de-prueba-local",
            ["Redsys:MerchantUrl"] = "https://localhost/api/v1/checkout/redsys/notification",
            ["Redsys:UrlOk"] = "http://localhost:4200/checkout/exito",
            ["Redsys:UrlKo"] = "http://localhost:4200/checkout/cancelado"
        };
    }

    [Fact]
    public void Does_not_offer_stripe_without_a_test_gateway_or_secret()
    {
        var catalog = new PaymentProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        Assert.Empty(catalog.Configured());
        Assert.Equal(PaymentProviderRules.RequiredMessage, Assert.Throws<ArgumentException>(() => catalog.EnsureSelectable(null)).Message);
        Assert.Throws<InvalidOperationException>(() => catalog.EnsureSelectable("Stripe"));
    }

    private static Order Place(long unitPriceCents)
    {
        return Order.CreatePending(
            Guid.NewGuid(),
            "DS-20261013-provider",
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, unitPriceCents, 21m, null)],
            CreatedAt);
    }
}
