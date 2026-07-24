using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class ForecastSnapshotLineConfiguration : IEntityTypeConfiguration<ForecastSnapshotLine>
{
    public void Configure(EntityTypeBuilder<ForecastSnapshotLine> builder)
    {
        builder.ToTable("forecast_snapshot_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(200);
        builder.HasOne(l => l.ForecastSnapshot).WithMany(s => s.Lines).HasForeignKey(l => l.ForecastSnapshotId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.ForecastSnapshotId);
    }
}
