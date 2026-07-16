using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> builder)
    {
        builder.ToTable("import_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Summary).HasMaxLength(2000);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(r => new { r.Source, r.RanAt });
    }
}
