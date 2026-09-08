using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class AmazonDepartmentMappingConfiguration : IEntityTypeConfiguration<AmazonDepartmentMapping>
{
    public void Configure(EntityTypeBuilder<AmazonDepartmentMapping> builder)
    {
        builder.ToTable("amazon_department_mappings");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.DepartmentName).IsRequired().HasMaxLength(200);
        builder.HasOne(m => m.Category).WithMany().HasForeignKey(m => m.CategoryId);
        // Case-insensitive uniqueness - the actual CREATE UNIQUE INDEX on lower(department_name)
        // is emitted by raw SQL in the migration (EF's fluent API can't express a functional index).
    }
}
