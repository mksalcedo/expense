namespace Expense.Domain.Services;

/// <summary>
/// Singleton signal raised whenever a sync/import completes (success or failure) - lets
/// any currently-open page react live (re-fetch its own data, or show a "new data
/// available" prompt) instead of only refreshing on navigation or a full page reload. See
/// SyncStatusProvider for where this gets raised. Same shape as the existing narrower
/// Expense.Web.Services.ReviewQueueChangeNotifier (Scoped, same-page-only) and
/// IStagedScrapeStore (Singleton, cross-request) - this generalizes the latter pattern to
/// every sync source and every page, not just one feature's own store.
/// </summary>
public interface IDataChangeNotifier
{
    event Action? Changed;
    void NotifyChanged();
}

public class DataChangeNotifier : IDataChangeNotifier
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
