using Stripe;

namespace DominioDeLaSierra.IntegrationTests;

internal static class StripeWebhookSignatures
{
    public const string Secret = "whsec_test_dominio_local";

    public static (string Body, string Header) Sign(string body)
    {
        return (body, EventUtility.GenerateSignatureHeader(body, Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    public static string Event(
        string eventId,
        string type,
        string sessionId,
        long amount,
        string paymentStatus = "paid",
        string currency = "eur",
        bool liveMode = false,
        string? orderId = null,
        string? paymentIntentId = null)
    {
        var reference = orderId is null ? "null" : $"\"{orderId}\"";
        var intentId = paymentIntentId ?? "pi_test_" + eventId.Replace("evt_test_", "", StringComparison.Ordinal);
        var intent = $"\"{intentId}\"";
        var metadata = orderId is null ? "{}" : $$"""{"order_id":"{{orderId}}"}""";
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2024-06-20",
          "created": 1710000000,
          "livemode": {{(liveMode ? "true" : "false")}},
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "payment_status": "{{paymentStatus}}",
              "status": "complete",
              "mode": "payment",
              "amount_total": {{amount}},
              "currency": "{{currency}}",
              "client_reference_id": {{reference}},
              "payment_intent": {{intent}},
              "metadata": {{metadata}}
            }
          }
        }
        """;
    }
}
