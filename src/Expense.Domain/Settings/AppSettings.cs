namespace Expense.Domain.Settings;

/// <summary>Bound via IOptions&lt;AppSettings&gt; - never a hardcoded constant, per the user's explicit requirement to test with shorter horizons during development.</summary>
public class AppSettings
{
    public int ForecastHorizonMonths { get; set; } = 12;
}
