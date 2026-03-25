using Microsoft.EntityFrameworkCore;
using FoodInspector.Models;

namespace FoodInspector.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ScanResult> ScanResults => Set<ScanResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<ScanResult>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
        });
    }
}
