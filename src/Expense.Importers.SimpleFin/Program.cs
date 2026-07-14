using Expense.Domain.Data;
using Expense.Domain.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Minimal wiring for now - just enough to seed the real database and verify Arc B
// against it. The actual SimpleFin pull (claim-token exchange, account sync) is Arc C.

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config.GetConnectionString("ExpenseDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ExpenseDb not set. Run: dotnet user-secrets set \"ConnectionStrings:ExpenseDb\" \"...\"");

var options = new DbContextOptionsBuilder<ExpenseDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var context = new ExpenseDbContext(options);
await context.Database.MigrateAsync();
await new DbSeeder().SeedAsync(context);

Console.WriteLine($"Seed complete. Categories: {await context.Categories.CountAsync()}, Accounts: {await context.Accounts.CountAsync()}");
