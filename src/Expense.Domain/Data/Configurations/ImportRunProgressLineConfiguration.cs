using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class ImportRunProgressLineConfiguration : IEntityTypeConfiguration<ImportRunProgressLine>
{
    public void Configure(EntityTypeBuilder<ImportRunProgressLine> builder)
    {
        builder.ToTable("import_run_progress_lines");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Text).IsRequired();
        builder.HasOne(p => p.ImportRun).WithMany(r => r.ProgressLines).HasForeignKey(p => p.ImportRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.ImportRunId, p.Sequence });
    }
}
