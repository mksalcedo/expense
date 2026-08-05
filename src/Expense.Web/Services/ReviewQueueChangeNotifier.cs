namespace Expense.Web.Services;

/// <summary>
/// Lets ReviewQueue.razor tell NavMenu.razor "the pending count may have changed" without a
/// full page navigation - registered Scoped (one instance per circuit) so both components
/// share the same instance while still isolating separate browser sessions.
/// </summary>
public interface IReviewQueueChangeNotifier
{
    event Action? Changed;
    void NotifyChanged();
}

public class ReviewQueueChangeNotifier : IReviewQueueChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
