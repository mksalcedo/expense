using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.Amazon;

public class AmazonDepartmentRulesPageData
{
    public required List<AmazonDepartmentMapping> Mappings { get; init; }
    public required List<Category> Categories { get; init; }
}

/// <summary>Thin abstraction over DepartmentMappingService so the /amazon-department-rules page can be tested against a fake.</summary>
public interface IAmazonDepartmentRulesPageProvider
{
    Task<AmazonDepartmentRulesPageData> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or re-points a department -> category rule, then re-resolves matching pending placeholders. Returns how many were auto-categorized as a result.</summary>
    Task<int> UpsertAsync(string departmentName, int categoryId, CancellationToken cancellationToken = default);

    Task DeleteAsync(int mappingId, CancellationToken cancellationToken = default);
}
