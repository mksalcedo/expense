using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class ForecastSnapshotConfiguration : IEntityTypeConfiguration<ForecastSnapshot>
{
    public void Configure(EntityTypeBuilder<ForecastSnapshot> builder)
    {
        builder.ToTable("forecast_snapshots");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.AsOfDate).IsUnique();
    }
}
