using System.Text.Json.Serialization;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

public class SimpleFinAccountsResponse
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    [JsonPropertyName("accounts")]
    public List<SimpleFinAccount> Accounts { get; set; } = [];
}

public class SimpleFinAccount
{
    [JsonPropertyName("org")]
    public SimpleFinOrg Org { get; set; } = new();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("balance")]
    [JsonConverter(typeof(DecimalStringConverter))]
    public decimal Balance { get; set; }

    [JsonPropertyName("balance-date")]
    public long BalanceDateUnix { get; set; }

    [JsonPropertyName("transactions")]
    public List<SimpleFinTransaction> Transactions { get; set; } = [];
}

public class SimpleFinOrg
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class SimpleFinTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("posted")]
    public long PostedUnix { get; set; }

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(DecimalStringConverter))]
    public decimal Amount { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}
