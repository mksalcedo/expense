using Expense.Domain.Data;
using Expense.Domain.Services.Accounts;
using Expense.Domain.Services.Backup;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Categories;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Export;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Services.HistoricalAnalysis;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Expense.Domain.Services.OneTimeEvents;
using Expense.Domain.Services.Scheduling;
using Expense.Domain.Services.SpendingTracker;
using Expense.Domain.Services.Transactions;
using Expense.Domain.Settings;
using Expense.Web.Components;
using Expense.Web.Services;
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
builder.Services.AddScoped<PartialPaymentService>();
builder.Services.AddScoped<IForecastResultProvider, ForecastResultProvider>();

builder.Services.AddScoped<ForecastAccuracyService>();
builder.Services.AddScoped<IForecastAccuracyPageProvider, ForecastAccuracyPageProvider>();
builder.Services.AddScoped<ForecastSnapshotService>();
builder.Services.AddScoped<IForecastHistoryPageProvider, ForecastHistoryPageProvider>();
builder.Services.AddScoped<TransactionReconciliationService>();
builder.Services.AddScoped<IConfirmedPaymentsPageProvider, ConfirmedPaymentsPageProvider>();

builder.Services.AddScoped<CategorizationService>();
builder.Services.AddHttpClient<IReviewQueueProvider, ReviewQueueProvider>();
builder.Services.AddScoped<IReviewQueueChangeNotifier, ReviewQueueChangeNotifier>();

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
builder.Services.AddScoped<ManualChargeMatchingService>();
builder.Services.AddHttpClient<SimpleFinSyncService>();
builder.Services.AddHttpClient<IManualChargesPageProvider, ManualChargesPageProvider>();
builder.Services.AddScoped<IPendingChargesPageProvider, PendingChargesPageProvider>();

builder.Services.AddScoped<AmazonOrderEmailParser>();
builder.Services.AddScoped<AmazonRefundEmailParser>();
builder.Services.AddScoped<AmazonImportService>();

builder.Services.AddScoped<SyncIssueService>();
builder.Services.AddScoped<ISyncStatusProvider, SyncStatusProvider>();

builder.Services.AddHostedService<SyncScheduler>();

builder.Services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddHostedService<BackupScheduler>();

builder.Services.AddScoped<TransactionManagementService>();
builder.Services.AddScoped<ITransactionsPageProvider, TransactionsPageProvider>();

builder.Services.AddSingleton<IStagedScrapeStore, StagedScrapeStore>();
builder.Services.AddScoped<IClipboardWatcherController, ClipboardWatcherController>();

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

// Called only by the local clipboard-watcher process (see
// docs/amazon-order-scraper-bookmarklet.md) - never exposed publicly, so it's restricted to
// loopback regardless of whatever's in front of this app (nginx, etc.), as defense in depth
// rather than relying solely on that not proxying this path.
app.MapPost("/internal/scraped-order-data", async (HttpContext context, IStagedScrapeStore store) =>
{
    if (!System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync();
    var staged = await store.TryStageAsync(json);
    return staged ? Results.Ok() : Results.BadRequest();
})
.DisableAntiforgery(); // called by the local watcher process, not a browser - no antiforgery token to present

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
