using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class OneTimeEventConfiguration : IEntityTypeConfiguration<OneTimeEvent>
{
    public void Configure(EntityTypeBuilder<OneTimeEvent> builder)
    {
        builder.ToTable("one_time_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Amount).HasColumnType("numeric(12,2)");
        builder.HasOne(e => e.Account).WithMany().HasForeignKey(e => e.AccountId);
        builder.HasIndex(e => e.Date);
    }
}
