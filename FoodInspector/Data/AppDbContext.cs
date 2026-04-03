using Microsoft.EntityFrameworkCore;
using FoodInspector.Models;

namespace FoodInspector.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ScanResult> ScanResults => Set<ScanResult>();
    public DbSet<FoodLog> FoodLogs => Set<FoodLog>();
    public DbSet<CustomIngredient> CustomIngredients => Set<CustomIngredient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(200);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<ScanResult>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
        });

        modelBuilder.Entity<FoodLog>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId);
            e.HasOne(f => f.ScanResult).WithMany().HasForeignKey(f => f.ScanResultId);
        });

        modelBuilder.Entity<CustomIngredient>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            e.HasIndex(c => new { c.UserId, c.Name }).IsUnique();
        });
    }
}
