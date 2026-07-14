using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.TestSupport;

/// <summary>
/// Applies migrations to the dedicated expense_test database once per test run.
/// Individual tests get their isolation from DatabaseTestBase's per-test transaction rollback,
/// not from this fixture.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public const string ConnectionString =
        "Host=localhost;Port=5433;Database=expense_test;Username=expense;Password=expense_dev_local_only";

    public static DbContextOptions<ExpenseDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<ExpenseDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    public async Task InitializeAsync()
    {
        await using var context = new ExpenseDbContext(BuildOptions());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
