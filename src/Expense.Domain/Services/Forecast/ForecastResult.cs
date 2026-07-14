namespace Expense.Domain.Services.Forecast;

public class ForecastLedgerRow
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required decimal RunningBalance { get; set; }
}

public class ForecastResult
{
    public required decimal StartingBalance { get; set; }
    public required List<ForecastLedgerRow> Rows { get; set; }

    public decimal LowestProjectedBalance =>
        Rows.Count == 0 ? StartingBalance : Rows.Min(r => r.RunningBalance);
}
