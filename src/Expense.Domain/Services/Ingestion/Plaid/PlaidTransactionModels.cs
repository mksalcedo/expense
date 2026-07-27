using System.Text.Json.Serialization;

namespace Expense.Domain.Services.Ingestion.Plaid;

public class PlaidTransactionsPayload
{
    [JsonPropertyName("accounts")]
    public List<PlaidAccountInfo> Accounts { get; set; } = [];

    [JsonPropertyName("transactions")]
    public List<PlaidTransaction> Transactions { get; set; } = [];
}

/// <summary>
/// The real shape of `plaid-cli transactions list --all --json` once more than one item
/// is linked - confirmed directly against real output, not guessed. Unlike the single-item
/// shape, there's no top-level "accounts"/"transactions" at all - each linked item gets its
/// own nested object (same accounts/transactions shape as PlaidTransactionsPayload) inside
/// a top-level "items" array. Flattened into one PlaidTransactionsPayload at parse time so
/// the rest of the import logic never needs to know which shape it came from.
/// </summary>
public class PlaidAllItemsPayload
{
    [JsonPropertyName("items")]
    public List<PlaidTransactionsPayload> Items { get; set; } = [];
}

public class PlaidAccountInfo
{
    [JsonPropertyName("account_id")]
    public string AccountId { get; set; } = "";

    [JsonPropertyName("balances")]
    public PlaidAccountBalances Balances { get; set; } = new();
}

public class PlaidAccountBalances
{
    /// <summary>
    /// Confirmed directly against real data as the figure comparable to what SimpleFin
    /// calls "balance" for this same account - "current" includes pending debit holds
    /// differently and did not match.
    /// </summary>
    [JsonPropertyName("available")]
    public decimal? Available { get; set; }

    [JsonPropertyName("current")]
    public decimal? Current { get; set; }
}

public class PlaidTransaction
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; } = "";

    [JsonPropertyName("account_id")]
    public string AccountId { get; set; } = "";

    /// <summary>Plaid's own convention: positive = money out, negative = money in - the
    /// opposite of this app's convention. Flipped at the point of import, not here.</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("date")]
    public DateOnly Date { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("merchant_name")]
    public string? MerchantName { get; set; }

    [JsonPropertyName("pending")]
    public bool Pending { get; set; }
}
