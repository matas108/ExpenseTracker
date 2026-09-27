using ExpenseTracker.Api.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ExpenseTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    // All DateTimes are stored as UTC; SQLite drops the Kind, so restore it on read.
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Account>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(100);
            e.Property(a => a.Currency).HasMaxLength(3);
            e.Property(a => a.OpeningBalance).HasPrecision(18, 2);
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(a => a.UserId);
        });

        builder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
        });

        builder.Entity<Transaction>(e =>
        {
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.ExternalId).HasMaxLength(200);
            e.HasIndex(t => new { t.UserId, t.Date });
            e.HasIndex(t => new { t.UserId, t.ExternalId });

            // Avoid multiple cascade paths from User (SQL Server rejects them).
            e.HasOne(t => t.Account).WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
