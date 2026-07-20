using Expense.Domain.Data;
using Expense.Domain.Services.Accounts;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Export;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Services.HistoricalAnalysis;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Expense.Domain.Services.OneTimeEvents;
using Expense.Domain.Services.SpendingTracker;
using Expense.Domain.Services.Transactions;
using Expense.Domain.Settings;
using Expense.Web.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
builder.Services.AddScoped<PaymentDeferralService>();
builder.Services.AddScoped<PaymentConfirmationService>();
builder.Services.AddScoped<IForecastResultProvider, ForecastResultProvider>();

builder.Services.AddScoped<CategorizationService>();
builder.Services.AddScoped<IReviewQueueProvider, ReviewQueueProvider>();

builder.Services.AddScoped<CategoryManagementService>();
builder.Services.AddScoped<ICategoriesPageProvider, CategoriesPageProvider>();

builder.Services.AddScoped<BudgetManagementService>();
builder.Services.AddScoped<IBudgetsPageProvider, BudgetsPageProvider>();

builder.Services.AddScoped<AccountManagementService>();
builder.Services.AddScoped<IAccountsPageProvider, AccountsPageProvider>();

builder.Services.AddScoped<OneTimeEventManagementService>();
builder.Services.AddScoped<IOneTimeEventsPageProvider, OneTimeEventsPageProvider>();

builder.Services.AddScoped<SpendingTrackerService>();
builder.Services.AddScoped<ISpendingTrackerPageProvider, SpendingTrackerPageProvider>();

builder.Services.AddScoped<HistoricalAnalysisService>();
builder.Services.AddScoped<IHistoricalAnalysisPageProvider, HistoricalAnalysisPageProvider>();

builder.Services.AddScoped<ForecastExcelExporter>();
builder.Services.AddSingleton<ExportFileNamer>();

builder.Services.AddScoped<DedupService>();
builder.Services.AddHttpClient<SimpleFinSyncService>();

builder.Services.AddScoped<AmazonOrderEmailParser>();
builder.Services.AddScoped<AmazonRefundEmailParser>();
builder.Services.AddScoped<AmazonImportService>();

builder.Services.AddScoped<SyncIssueService>();
builder.Services.AddScoped<ISyncStatusProvider, SyncStatusProvider>();

builder.Services.AddScoped<TransactionManagementService>();
builder.Services.AddScoped<ITransactionsPageProvider, TransactionsPageProvider>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapGet("/export/forecast.xlsx", async (
    IDbContextFactory<ExpenseDbContext> contextFactory, ForecastExcelExporter exporter, ExportFileNamer fileNamer, IOptions<AppSettings> options) =>
{
    await using var context = await contextFactory.CreateDbContextAsync();
    var asOfDate = DateOnly.FromDateTime(DateTime.Today);
    var windowEnd = asOfDate.AddMonths(options.Value.ForecastHorizonMonths);

    using var workbook = await exporter.ExportAsync(context, asOfDate, windowEnd);
    using var stream = new MemoryStream();
    workbook.SaveAs(stream);

    return Results.File(stream.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileNamer.GetNextFileName(asOfDate));
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
