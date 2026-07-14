using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Expense.Domain.Tests.TestSupport;

/// <summary>
/// Base class for entity round-trip / query tests against a real Postgres database.
/// Every test runs inside a transaction that is rolled back on dispose, so the
/// expense_test database stays clean between tests without manual cleanup.
/// </summary>
[Collection("Database")]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private IDbContextTransaction _transaction = null!;

    protected ExpenseDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Context = new ExpenseDbContext(DatabaseFixture.BuildOptions());
        await Context.Database.OpenConnectionAsync();
        _transaction = await Context.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await Context.DisposeAsync();
    }

    /// <summary>
    /// A second context sharing the same open transaction, so a "reload" genuinely
    /// re-queries the database instead of reading from the first context's change tracker.
    /// </summary>
    protected ExpenseDbContext CreateContextInSameTransaction()
    {
        var ctx = new ExpenseDbContext(DatabaseFixture.BuildOptions());
        ctx.Database.SetDbConnection(Context.Database.GetDbConnection());
        ctx.Database.UseTransaction(_transaction.GetDbTransaction());
        return ctx;
    }
}
