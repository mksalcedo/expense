namespace Expense.Domain.Entities;

/// <summary>
/// The Amazon product -> category lookup table. A NULL match here (no row found for
/// a scraped item title) is what puts an amazon_order_items row into pending categorization.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public required string ProductPattern { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
