using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Data;

public class ExpenseDbContext(DbContextOptions<ExpenseDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CheckingBalanceSnapshot> CheckingBalanceSnapshots => Set<CheckingBalanceSnapshot>();
    public DbSet<DebtBalanceSnapshot> DebtBalanceSnapshots => Set<DebtBalanceSnapshot>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<FundingRule> FundingRules => Set<FundingRule>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<AmazonOrderItem> AmazonOrderItems => Set<AmazonOrderItem>();
    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();
    public DbSet<OneTimeEvent> OneTimeEvents => Set<OneTimeEvent>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<PaymentDeferral> PaymentDeferrals => Set<PaymentDeferral>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpenseDbContext).Assembly);
    }
}
