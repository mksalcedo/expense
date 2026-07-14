using Expense.Domain.Data;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Settings;
using Expense.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("ExpenseDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ExpenseDb not set. Run: dotnet user-secrets set \"ConnectionStrings:ExpenseDb\" \"...\" --project src/Expense.Web");

builder.Services.AddDbContextFactory<ExpenseDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

builder.Services.AddScoped<BudgetProrationService>();
builder.Services.AddScoped<RecurrenceExpander>();
builder.Services.AddScoped<AmexCycleCalculator>();
builder.Services.AddScoped<ForecastEngine>();
builder.Services.AddScoped<IForecastResultProvider, ForecastResultProvider>();

builder.Services.AddScoped<CategorizationService>();
builder.Services.AddScoped<IReviewQueueProvider, ReviewQueueProvider>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
