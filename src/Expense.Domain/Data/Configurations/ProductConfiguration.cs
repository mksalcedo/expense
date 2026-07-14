using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ProductPattern).IsRequired().HasMaxLength(300);
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
    }
}
