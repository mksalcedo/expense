namespace Expense.Domain.Services.Forecast;

/// <summary>Thin abstraction over PaymentConfirmationService so UI components can be tested against a fake result.</summary>
public interface IConfirmedPaymentsPageProvider
{
    Task<List<ConfirmedPaymentRow>> GetConfirmedPaymentsAsync(CancellationToken cancellationToken = default);

    Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default);
}
