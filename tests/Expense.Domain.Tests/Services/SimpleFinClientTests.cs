using System.Net;
using System.Text;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services;

public class SimpleFinClientTests
{
    private const string SampleResponse = """
    {
      "errors": ["Requested date range exceeds recommended range of 45 days. In the future, this may be capped."],
      "accounts": [
        {
          "org": { "name": "Wells Fargo" },
          "name": "EVERYDAY CHECKING ...4103 (4103)",
          "balance": "6463.02",
          "currency": "USD",
          "balance-date": 1783980195,
          "transactions": [
            { "id": "tx-1", "posted": 1783684800, "amount": "-2334.15", "description": "AMEX EPAYMENT ACH PMT" }
          ]
        }
      ]
    }
    """;

    private static SimpleFinClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), "https://user123:pass456@beta-bridge.simplefin.org/simplefin/access/abc");

    [Fact]
    public async Task GetAccountsAsync_RequestsTheAccountsEndpoint_WithStartDateAsUnixTimestamp()
    {
        var handler = new FakeHttpMessageHandler(SampleResponse);
        var client = CreateClient(handler);
        var startDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await client.GetAccountsAsync(startDate);

        Assert.NotNull(handler.CapturedRequest);
        var uri = handler.CapturedRequest!.RequestUri!;
        Assert.EndsWith("/simplefin/access/abc/accounts", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains($"start-date={startDate.ToUnixTimeSeconds()}", uri.Query);
    }

    [Fact]
    public async Task GetAccountsAsync_SendsBasicAuthHeader_DerivedFromTheAccessUrlCredentials()
    {
        var handler = new FakeHttpMessageHandler(SampleResponse);
        var client = CreateClient(handler);

        await client.GetAccountsAsync(DateTimeOffset.UtcNow);

        var authHeader = handler.CapturedRequest!.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Basic", authHeader!.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Parameter!));
        Assert.Equal("user123:pass456", decoded);
    }

    [Fact]
    public async Task GetAccountsAsync_NeverPutsCredentialsInTheRequestUri()
    {
        var handler = new FakeHttpMessageHandler(SampleResponse);
        var client = CreateClient(handler);

        await client.GetAccountsAsync(DateTimeOffset.UtcNow);

        Assert.DoesNotContain("user123", handler.CapturedRequest!.RequestUri!.ToString());
        Assert.DoesNotContain("pass456", handler.CapturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAccountsAsync_DeserializesAccountsAndTransactions_ConvertingStringAmountsToDecimal()
    {
        var handler = new FakeHttpMessageHandler(SampleResponse);
        var client = CreateClient(handler);

        var result = await client.GetAccountsAsync(DateTimeOffset.UtcNow);

        var account = Assert.Single(result.Accounts);
        Assert.Equal("Wells Fargo", account.Org.Name);
        Assert.Equal(6463.02m, account.Balance);
        var transaction = Assert.Single(account.Transactions);
        Assert.Equal(-2334.15m, transaction.Amount);
        Assert.Equal("AMEX EPAYMENT ACH PMT", transaction.Description);
    }

    [Fact]
    public async Task GetAccountsAsync_SurfacesErrorsArray_WithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(SampleResponse);
        var client = CreateClient(handler);

        var result = await client.GetAccountsAsync(DateTimeOffset.UtcNow);

        Assert.Single(result.Errors);
        Assert.Contains("45 days", result.Errors[0]);
    }
}
