using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Expense.Domain.Data;

/// <summary>
/// Used only by the `dotnet ef` CLI to generate/apply migrations at design time.
/// The connection string here is never used at runtime - Expense.Web and the
/// importers configure ExpenseDbContext themselves via dependency injection.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ExpenseDbContext>
{
    public ExpenseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExpenseDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=expense_test;Username=expense;Password=expense_dev_local_only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ExpenseDbContext(options);
    }
}
