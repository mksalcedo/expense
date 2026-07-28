using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.ToTable("backup_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.FilePath).HasMaxLength(500);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(r => r.RanAt);
    }
}
