using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Notebook> Notebooks { get; set; } = null!;
    public DbSet<Source> Sources { get; set; } = null!;
    public DbSet<Note> Notes { get; set; } = null!;
    public DbSet<ChatSession> ChatSessions { get; set; } = null!;

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

        modelBuilder.Entity<Notebook>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(36);
            e.Property(n => n.UserId).HasMaxLength(36).IsRequired();
            e.Property(n => n.Name).HasMaxLength(255).IsRequired();
            e.Property(n => n.Description).HasMaxLength(1000);
            e.Property(n => n.CreatedAt).HasColumnType("datetime");
            e.Property(n => n.UpdatedAt).HasColumnType("datetime");
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.UserId);
        });

        modelBuilder.Entity<Source>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(36);
            e.Property(s => s.UserId).HasMaxLength(36).IsRequired();
            e.Property(s => s.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(s => s.Title).HasMaxLength(512).IsRequired();
            e.Property(s => s.FilePath).HasMaxLength(1024);
            e.Property(s => s.MimeType).HasMaxLength(128);
            e.Property(s => s.Status).HasMaxLength(32);
            e.Property(s => s.CreatedAt).HasColumnType("datetime");
            e.Property(s => s.UpdatedAt).HasColumnType("datetime");
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Notebook).WithMany(n => n.Sources).HasForeignKey(s => s.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.NotebookId);
        });

        modelBuilder.Entity<Note>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(36);
            e.Property(n => n.UserId).HasMaxLength(36).IsRequired();
            e.Property(n => n.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(n => n.Title).HasMaxLength(512);
            e.Property(n => n.Content).HasColumnType("text");
            e.Property(n => n.NoteType).HasMaxLength(16);
            e.Property(n => n.CreatedAt).HasColumnType("datetime");
            e.Property(n => n.UpdatedAt).HasColumnType("datetime");
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Notebook).WithMany(nb => nb.Notes).HasForeignKey(n => n.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.NotebookId);
        });

        modelBuilder.Entity<ChatSession>(e =>
        {
            e.HasKey(cs => cs.Id);
            e.Property(cs => cs.Id).HasMaxLength(36);
            e.Property(cs => cs.UserId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.Title).HasMaxLength(512);
            e.Property(cs => cs.CreatedAt).HasColumnType("datetime");
            e.Property(cs => cs.UpdatedAt).HasColumnType("datetime");
            e.HasOne(cs => cs.User).WithMany().HasForeignKey(cs => cs.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cs => cs.Notebook).WithMany(n => n.ChatSessions).HasForeignKey(cs => cs.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(cs => cs.NotebookId);
        });
    }
}
