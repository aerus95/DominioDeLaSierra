using System.Text;
using DominioDeLaSierra.Application.Checkout;

namespace DominioDeLaSierra.UnitTests;

public class RedsysSignatureTests
{
    private const string ExampleKey = "sq7HjrUOBfKmC576ILgskD5srU870gJ7";

    private const string SigningPageParameters =
        "eyJEU19NRVJDSEFOVF9BTU9VTlQiOiI5OTkiLCJEU19NRVJDSEFOVF9PUkRFUiI6IjEyMzQ1Njc4OTAiLCJEU19NRVJDSEFOVF9NRVJDSEFOVENPREUiOiI5OTkwMDg4ODEiLCJEU19NRVJDSEFOVF9DVVJSRU5DWSI6Ijk3OCIsIkRTX01FUkNIQU5UX1RSQU5TQUNUSU9OVFlQRSI6IjAiLCJEU19NRVJDSEFOVF9URVJNSU5BTCI6IjEiLCJEU19NRVJDSEFOVF9NRVJDSEFOVFVSTCI6Imh0dHA6XC9cL3d3dy5wcnVlYmEuY29tXC91cmxOb3RpZmljYWNpb24ucGhwIiwiRFNfTUVSQ0hBTlRfVVJMT0siOiJodHRwOlwvXC93d3cucHJ1ZWJhLmNvbVwvdXJsT0sucGhwIiwiRFNfTUVSQ0hBTlRfVVJMS08iOiJodHRwOlwvXC93d3cucHJ1ZWJhLmNvbVwvdXJsS08ucGhwIn0";

    private const string RedirectParameters =
        "eyJEU19NRVJDSEFOVF9PUkRFUiI6IjAwMDE0MzM4ejc0NCIsIkRTX01FUkNIQU5UX01FUkNIQU5UQ09ERSI6Ijk5OTAwODg4MSIsIkRTX01FUkNIQU5UX1RFUk1JTkFMIjoiMDAxIiwiRFNfTUVSQ0hBTlRfQ1VSUkVOQ1kiOiI5NzgiLCJEU19NRVJDSEFOVF9UUkFOU0FDVElPTlRZUEUiOiIwIiwiRFNfTUVSQ0hBTlRfQU1PVU5UIjoiMjQ5IiwiRFNfTUVSQ0hBTlRfTUVSQ0hBTlRVUkwiOiJodHRwOi8vd3d3LnBydWViYS5jb20vdXJsTm90aWZpY2FjaW9uLnBocCIsIkRTX01FUkNIQU5UX1VSTE9LIjoiaHR0cDovL3d3dy5wcnVlYmEuY29tL3VybE9LLnBocCIsIkRTX01FUkNIQU5UX1VSTEtPIjoiaHR0cDovL3d3dy5wcnVlYmEuY29tL3VybEtPLnBocCJ9";

    [Fact]
    public void Reproduces_the_official_hmac_sha512_v2_examples()
    {
        Assert.Equal("RWt3/IPTzYRMXsQtkiGRKg==", Convert.ToBase64String(RedsysSignature.Diversify("1234567890", ExampleKey)));
        Assert.Equal(
            "Vjo02eSWq249IeZZp3R-ArFnGLhKY0OuzDDlx1BuVtZDC2yhczA7_11uZhsYzLZBCMFAz8u8uzGDX3AErHKmmw",
            RedsysSignature.Sign(SigningPageParameters, "1234567890", ExampleKey));
        Assert.Equal(
            "DfXW23aJNwzzLH2IoV5c9YiRKOf4cUgTbd4XtDIxDM_P-XEr79S3cQy_GZUl6P2Sf-udXwRGI0_NoLJ3ChIJkg",
            RedsysSignature.Sign(RedirectParameters, "00014338z744", ExampleKey));
    }

    [Fact]
    public void Rejects_a_signature_of_parameters_that_were_changed()
    {
        var signed = RedsysSignature.Sign(RedirectParameters, "00014338z744", ExampleKey);
        var changed = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"DS_MERCHANT_AMOUNT\":\"1\"}"));

        Assert.True(RedsysSignature.Matches(RedirectParameters, "00014338z744", ExampleKey, signed));
        Assert.False(RedsysSignature.Matches(changed, "00014338z744", ExampleKey, signed));
        Assert.False(RedsysSignature.Matches(RedirectParameters, "00014338z745", ExampleKey, signed));
    }

    [Theory]
    [InlineData("0000", RedsysOutcome.Authorized)]
    [InlineData("0", RedsysOutcome.Authorized)]
    [InlineData("0001", RedsysOutcome.Unknown)]
    [InlineData("0099", RedsysOutcome.Unknown)]
    [InlineData("99", RedsysOutcome.Unknown)]
    [InlineData("0100", RedsysOutcome.Denied)]
    [InlineData("0190", RedsysOutcome.Denied)]
    [InlineData("0184", RedsysOutcome.Denied)]
    [InlineData("0400", RedsysOutcome.Unknown)]
    [InlineData("400", RedsysOutcome.Unknown)]
    [InlineData("0900", RedsysOutcome.Unknown)]
    [InlineData("900", RedsysOutcome.Unknown)]
    [InlineData("SIS0001", RedsysOutcome.Unknown)]
    [InlineData("00a0", RedsysOutcome.Unknown)]
    [InlineData("", RedsysOutcome.Unknown)]
    [InlineData(null, RedsysOutcome.Unknown)]
    public void Classifies_purchase_authorization_denial_and_ambiguous_codes(string? response, RedsysOutcome outcome)
    {
        Assert.Equal(outcome, RedsysResponse.Classify(response));
        Assert.True(RedsysParameterReader.SameTerminal("001", "1"));
        Assert.False(RedsysParameterReader.SameTerminal("001", "002"));
    }
}
