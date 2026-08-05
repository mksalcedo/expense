using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        // ValueGeneratedNever: without it, inserting a new Category with IsActive explicitly
        // set to false (the bool CLR default) would be silently overridden by the column
        // default (true) - the same EF gotcha hit with BudgetPeriod.Direction/Income.
        builder.Property(c => c.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        // Same ValueGeneratedNever gotcha as IsActive above: null is a legitimate, explicit
        // "no cap" value here, and without this an explicit null would get silently
        // overwritten by the 1.0 default on insert.
        builder.Property(c => c.CarryoverCapMultiplier).HasDefaultValue(1.0m).ValueGeneratedNever();
        // Deliberately DB-generated (not set in C#): a new category has no carryover history
        // before it existed, so "the day it was created" is always correct, and an existing
        // category's own migration backfill needs the single snapshot value CURRENT_DATE
        // gives at ALTER TABLE time - see Category.CarryoverAnchorDate.
        builder.Property(c => c.CarryoverAnchorDate).HasDefaultValueSql("CURRENT_DATE");
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
