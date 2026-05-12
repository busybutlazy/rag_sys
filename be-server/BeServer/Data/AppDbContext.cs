using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasMaxLength(36);
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.CreatedAt).HasColumnType("datetime");
            e.Property(u => u.UpdatedAt).HasColumnType("datetime");
        });
    }
}
