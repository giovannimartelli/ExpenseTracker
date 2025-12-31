using System.Text.Json;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Data.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        
        builder.HasOne(e => e.SubCategory)
            .WithMany(c => c.Expenses)
            .HasForeignKey(e => e.SubCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
                l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                l => l.ToList()));
    }
}