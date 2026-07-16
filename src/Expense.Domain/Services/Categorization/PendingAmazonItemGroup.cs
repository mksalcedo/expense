namespace Expense.Domain.Services.Categorization;

public class PendingAmazonItemGroup
{
    public required string SuggestedPattern { get; set; }
    public required string ItemTitle { get; set; }
    public required DateOnly SampleDate { get; set; }
    public required List<int> ItemIds { get; set; }
    public decimal TotalPrice { get; set; }
}
