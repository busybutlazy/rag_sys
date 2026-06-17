using BeServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BeServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Notebook> Notebooks { get; set; } = null!;
    public DbSet<Source> Sources { get; set; } = null!;
    public DbSet<IngestionJob> IngestionJobs { get; set; } = null!;
    public DbSet<Note> Notes { get; set; } = null!;
    public DbSet<ChatSession> ChatSessions { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<ChatRequest> ChatRequests { get; set; } = null!;
    public DbSet<RequestLog> RequestLogs { get; set; } = null!;
    public DbSet<SessionTask> SessionTasks { get; set; } = null!;
    public DbSet<RetrievalPreset> RetrievalPresets { get; set; } = null!;
    public DbSet<NotebookRetrievalVersion> NotebookRetrievalVersions { get; set; } = null!;
    public DbSet<ReindexJob> ReindexJobs { get; set; } = null!;
    public DbSet<EvaluationDataset> EvaluationDatasets { get; set; } = null!;
    public DbSet<EvaluationQuery> EvaluationQueries { get; set; } = null!;
    public DbSet<EvaluationRun> EvaluationRuns { get; set; } = null!;
    public DbSet<EvaluationResult> EvaluationResults { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasMaxLength(36);
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(u => u.IsDevAdmin).HasDefaultValue(false);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.CreatedAt).HasColumnType("datetime");
            e.Property(u => u.UpdatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasMaxLength(36);
            e.Property(t => t.UserId).HasMaxLength(36).IsRequired();
            e.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(t => t.FamilyId).HasMaxLength(36).IsRequired();
            e.Property(t => t.ExpiresAt).HasColumnType("datetime");
            e.Property(t => t.RevokedAt).HasColumnType("datetime");
            e.Property(t => t.ReplacedByTokenId).HasMaxLength(36);
            e.Property(t => t.CreatedAt).HasColumnType("datetime");
            e.Property(t => t.CreatedByIp).HasMaxLength(64);
            e.Property(t => t.RevokedByIp).HasMaxLength(64);
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasIndex(t => t.FamilyId);
            e.HasIndex(t => t.ExpiresAt);
        });

        modelBuilder.Entity<Notebook>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasMaxLength(36);
            e.Property(n => n.UserId).HasMaxLength(36).IsRequired();
            e.Property(n => n.Name).HasMaxLength(255).IsRequired();
            e.Property(n => n.Description).HasMaxLength(1000);
            e.Property(n => n.ActiveRetrievalVersionId).HasMaxLength(36);
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
            e.Property(s => s.OriginalContentType).HasMaxLength(128);
            e.Property(s => s.DetectedMimeType).HasMaxLength(128);
            e.Property(s => s.Status).HasMaxLength(32);
            e.Property(s => s.ActiveRetrievalVersionId).HasMaxLength(36);
            e.Property(s => s.LastIndexedRetrievalVersionId).HasMaxLength(36);
            e.Property(s => s.CreatedAt).HasColumnType("datetime");
            e.Property(s => s.UpdatedAt).HasColumnType("datetime");
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Notebook).WithMany(n => n.Sources).HasForeignKey(s => s.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
            e.HasIndex(s => s.NotebookId);
            e.HasIndex(s => new { s.UserId, s.NotebookId });
        });

        modelBuilder.Entity<IngestionJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Id).HasMaxLength(36);
            e.Property(j => j.SourceId).HasMaxLength(36).IsRequired();
            e.Property(j => j.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(j => j.UserId).HasMaxLength(36).IsRequired();
            e.Property(j => j.JobType).HasMaxLength(32).IsRequired();
            e.Property(j => j.Status).HasMaxLength(32).IsRequired();
            e.Property(j => j.LastError).HasColumnType("text");
            e.Property(j => j.GraphExtractionStatus).HasMaxLength(16);
            e.Property(j => j.AvailableAt).HasColumnType("datetime");
            e.Property(j => j.StartedAt).HasColumnType("datetime");
            e.Property(j => j.CompletedAt).HasColumnType("datetime");
            e.Property(j => j.CreatedAt).HasColumnType("datetime");
            e.Property(j => j.UpdatedAt).HasColumnType("datetime");
            e.HasOne(j => j.User).WithMany().HasForeignKey(j => j.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.Notebook).WithMany().HasForeignKey(j => j.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => j.SourceId);
            e.HasIndex(j => new { j.Status, j.JobType, j.AvailableAt });
            e.HasIndex(j => new { j.UserId, j.NotebookId });
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
            e.HasIndex(n => n.UserId);
            e.HasIndex(n => n.NotebookId);
            e.HasIndex(n => new { n.UserId, n.NotebookId });
        });

        modelBuilder.Entity<ChatSession>(e =>
        {
            e.HasKey(cs => cs.Id);
            e.Property(cs => cs.Id).HasMaxLength(36);
            e.Property(cs => cs.UserId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(cs => cs.Title).HasMaxLength(512);
            e.Property(cs => cs.Mode).HasMaxLength(32).HasDefaultValue("chat");
            e.Property(cs => cs.SessionStateJson).HasColumnType("json");
            e.Property(cs => cs.ActiveTaskId).HasMaxLength(36);
            e.Property(cs => cs.Archived).HasDefaultValue(false);
            e.Property(cs => cs.LastMessageAt).HasColumnType("datetime");
            e.Property(cs => cs.CreatedAt).HasColumnType("datetime");
            e.Property(cs => cs.UpdatedAt).HasColumnType("datetime");
            e.HasOne(cs => cs.User).WithMany().HasForeignKey(cs => cs.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cs => cs.Notebook).WithMany(n => n.ChatSessions).HasForeignKey(cs => cs.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SessionTask>().WithMany().HasForeignKey(cs => cs.ActiveTaskId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(cs => cs.UserId);
            e.HasIndex(cs => cs.NotebookId);
            e.HasIndex(cs => new { cs.UserId, cs.NotebookId });
            e.HasIndex(cs => cs.ActiveTaskId);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasMaxLength(36);
            e.Property(m => m.SessionId).HasMaxLength(36).IsRequired();
            e.Property(m => m.UserId).HasMaxLength(36).IsRequired();
            e.Property(m => m.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(m => m.Role).HasMaxLength(16).IsRequired();
            e.Property(m => m.Content).HasColumnType("longtext");
            e.Property(m => m.ContentPreview).HasMaxLength(150).IsRequired();
            e.Property(m => m.RequestId).HasMaxLength(36);
            e.Property(m => m.SourcesJson).HasColumnType("json");
            e.Property(m => m.TracesJson).HasColumnType("json");
            e.Property(m => m.MetadataJson).HasColumnType("json");
            e.Property(m => m.CreatedAt).HasColumnType("datetime");
            e.HasOne(m => m.Session).WithMany(s => s.Messages).HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Notebook).WithMany().HasForeignKey(m => m.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChatRequest>().WithMany().HasForeignKey(m => m.RequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(m => m.SessionId);
            e.HasIndex(m => new { m.SessionId, m.Sequence }).IsUnique();
            e.HasIndex(m => new { m.UserId, m.NotebookId });
            e.HasIndex(m => m.RequestId);
        });

        modelBuilder.Entity<ChatRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(36);
            e.Property(r => r.SessionId).HasMaxLength(36).IsRequired();
            e.Property(r => r.UserMessageId).HasMaxLength(36);
            e.Property(r => r.AssistantMessageId).HasMaxLength(36);
            e.Property(r => r.Mode).HasMaxLength(32).IsRequired();
            e.Property(r => r.Model).HasMaxLength(128).IsRequired();
            e.Property(r => r.RetrievalVersionId).HasMaxLength(36);
            e.Property(r => r.Status).HasMaxLength(32).IsRequired();
            e.Property(r => r.ContextSnapshotJson).HasColumnType("json");
            e.Property(r => r.Error).HasColumnType("text");
            e.Property(r => r.StartedAt).HasColumnType("datetime");
            e.Property(r => r.CompletedAt).HasColumnType("datetime");
            e.HasOne(r => r.Session).WithMany(s => s.Requests).HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.SessionId);
            e.HasIndex(r => r.UserMessageId);
        });

        modelBuilder.Entity<RequestLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasMaxLength(36);
            e.Property(l => l.ChatRequestId).HasMaxLength(36);
            e.Property(l => l.SessionId).HasMaxLength(36);
            e.Property(l => l.Direction).HasMaxLength(32).IsRequired();
            e.Property(l => l.Service).HasMaxLength(64).IsRequired();
            e.Property(l => l.Operation).HasMaxLength(128).IsRequired();
            e.Property(l => l.Method).HasMaxLength(16);
            e.Property(l => l.Url).HasMaxLength(2048);
            e.Property(l => l.RequestJson).HasColumnType("json");
            e.Property(l => l.ResponseJson).HasColumnType("json");
            e.Property(l => l.Error).HasColumnType("text");
            e.Property(l => l.CreatedAt).HasColumnType("datetime");
            e.HasOne(l => l.ChatRequest).WithMany(r => r.Logs).HasForeignKey(l => l.ChatRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.Session).WithMany().HasForeignKey(l => l.SessionId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => l.ChatRequestId);
            e.HasIndex(l => l.SessionId);
            e.HasIndex(l => new { l.Service, l.Operation });
            e.HasIndex(l => l.CreatedAt);
        });

        modelBuilder.Entity<SessionTask>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasMaxLength(36);
            e.Property(t => t.SessionId).HasMaxLength(36).IsRequired();
            e.Property(t => t.Title).HasMaxLength(512).IsRequired();
            e.Property(t => t.Summary).HasColumnType("text");
            e.Property(t => t.Status).HasMaxLength(32).IsRequired();
            e.Property(t => t.StateJson).HasColumnType("json");
            e.Property(t => t.CreatedFromRequestId).HasMaxLength(36);
            e.Property(t => t.UpdatedFromRequestId).HasMaxLength(36);
            e.Property(t => t.CreatedAt).HasColumnType("datetime");
            e.Property(t => t.UpdatedAt).HasColumnType("datetime");
            e.Property(t => t.CompletedAt).HasColumnType("datetime");
            e.HasOne(t => t.Session).WithMany(s => s.Tasks).HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChatRequest>().WithMany().HasForeignKey(t => t.CreatedFromRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ChatRequest>().WithMany().HasForeignKey(t => t.UpdatedFromRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => t.SessionId);
            e.HasIndex(t => new { t.SessionId, t.Status });
            e.HasIndex(t => t.CreatedFromRequestId);
            e.HasIndex(t => t.UpdatedFromRequestId);
        });

        modelBuilder.Entity<RetrievalPreset>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(36);
            e.Property(p => p.Key).HasMaxLength(64).IsRequired();
            e.Property(p => p.Name).HasMaxLength(128).IsRequired();
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.EmbeddingModel).HasMaxLength(128).IsRequired();
            e.Property(p => p.DefaultSearchMode).HasMaxLength(32).IsRequired();
            e.Property(p => p.CreatedAt).HasColumnType("datetime");
            e.Property(p => p.UpdatedAt).HasColumnType("datetime");
            e.HasIndex(p => p.Key).IsUnique();
        });

        modelBuilder.Entity<NotebookRetrievalVersion>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasMaxLength(36);
            e.Property(v => v.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(v => v.CreatedByUserId).HasMaxLength(36).IsRequired();
            e.Property(v => v.ParentVersionId).HasMaxLength(36);
            e.Property(v => v.OriginPresetId).HasMaxLength(36);
            e.Property(v => v.EmbeddingModel).HasMaxLength(128).IsRequired();
            e.Property(v => v.DefaultSearchMode).HasMaxLength(32).IsRequired();
            e.Property(v => v.GraphExtractionModel).HasMaxLength(128);
            e.Property(v => v.MaxGraphHops).HasDefaultValue(1);
            e.Property(v => v.MaxFactHits).HasDefaultValue(8);
            e.Property(v => v.Notes).HasMaxLength(1000);
            e.Property(v => v.CreatedAt).HasColumnType("datetime");
            e.HasOne(v => v.Notebook).WithMany(n => n.RetrievalVersions).HasForeignKey(v => v.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.CreatedByUser).WithMany().HasForeignKey(v => v.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => v.NotebookId);
            e.HasIndex(v => v.ParentVersionId);
            e.HasIndex(v => v.OriginPresetId);
        });

        modelBuilder.Entity<ReindexJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Id).HasMaxLength(36);
            e.Property(j => j.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(j => j.UserId).HasMaxLength(36).IsRequired();
            e.Property(j => j.SourceId).HasMaxLength(36);
            e.Property(j => j.Scope).HasMaxLength(16).IsRequired();
            e.Property(j => j.TargetRetrievalVersionId).HasMaxLength(36).IsRequired();
            e.Property(j => j.PreviousRetrievalVersionId).HasMaxLength(36);
            e.Property(j => j.Status).HasMaxLength(16).IsRequired();
            e.Property(j => j.LastError).HasColumnType("text");
            e.Property(j => j.GraphExtractionStatus).HasMaxLength(16);
            e.Property(j => j.AvailableAt).HasColumnType("datetime");
            e.Property(j => j.StartedAt).HasColumnType("datetime");
            e.Property(j => j.CompletedAt).HasColumnType("datetime");
            e.Property(j => j.CreatedAt).HasColumnType("datetime");
            e.Property(j => j.UpdatedAt).HasColumnType("datetime");
            e.HasOne(j => j.Notebook).WithMany().HasForeignKey(j => j.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.User).WithMany().HasForeignKey(j => j.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(j => j.NotebookId);
            e.HasIndex(j => new { j.Status, j.AvailableAt });
            e.HasIndex(j => new { j.UserId, j.NotebookId });
        });

        modelBuilder.Entity<EvaluationDataset>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasMaxLength(36);
            e.Property(d => d.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(d => d.UserId).HasMaxLength(36).IsRequired();
            e.Property(d => d.Name).HasMaxLength(160).IsRequired();
            e.Property(d => d.Description).HasColumnType("text");
            e.Property(d => d.CreatedAt).HasColumnType("datetime");
            e.Property(d => d.UpdatedAt).HasColumnType("datetime");
            e.HasOne(d => d.Notebook).WithMany().HasForeignKey(d => d.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.UserId, d.NotebookId });
        });

        modelBuilder.Entity<EvaluationQuery>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.Id).HasMaxLength(36);
            e.Property(q => q.DatasetId).HasMaxLength(36).IsRequired();
            e.Property(q => q.QueryText).HasMaxLength(500).IsRequired();
            e.Property(q => q.ExpectedAnswerNotes).HasColumnType("text");
            e.Property(q => q.GoldSourceNotes).HasColumnType("text");
            e.Property(q => q.CreatedAt).HasColumnType("datetime");
            e.Property(q => q.UpdatedAt).HasColumnType("datetime");
            e.HasOne(q => q.Dataset).WithMany(d => d.Queries).HasForeignKey(q => q.DatasetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(q => new { q.DatasetId, q.SortOrder });
        });

        modelBuilder.Entity<EvaluationRun>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(36);
            e.Property(r => r.NotebookId).HasMaxLength(36).IsRequired();
            e.Property(r => r.DatasetId).HasMaxLength(36);
            e.Property(r => r.UserId).HasMaxLength(36).IsRequired();
            e.Property(r => r.RetrievalVersionAId).HasMaxLength(36).IsRequired();
            e.Property(r => r.RetrievalVersionBId).HasMaxLength(36).IsRequired();
            e.Property(r => r.SearchModesJson).HasColumnType("json");
            e.Property(r => r.Status).HasMaxLength(16).IsRequired();
            e.Property(r => r.StartedAt).HasColumnType("datetime");
            e.Property(r => r.CompletedAt).HasColumnType("datetime");
            e.Property(r => r.CreatedAt).HasColumnType("datetime");
            e.HasOne(r => r.Notebook).WithMany().HasForeignKey(r => r.NotebookId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Dataset).WithMany().HasForeignKey(r => r.DatasetId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.UserId, r.NotebookId, r.CreatedAt });
        });

        modelBuilder.Entity<EvaluationResult>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(36);
            e.Property(r => r.RunId).HasMaxLength(36).IsRequired();
            e.Property(r => r.QueryId).HasMaxLength(36);
            e.Property(r => r.QueryTextSnapshot).HasMaxLength(500).IsRequired();
            e.Property(r => r.RetrievalVersionId).HasMaxLength(36).IsRequired();
            e.Property(r => r.Mode).HasMaxLength(16).IsRequired();
            e.Property(r => r.ResultsJson).HasColumnType("json");
            e.Property(r => r.CreatedAt).HasColumnType("datetime");
            e.HasOne(r => r.Run).WithMany(run => run.Results).HasForeignKey(r => r.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Query).WithMany().HasForeignKey(r => r.QueryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => r.RunId);
        });
    }
}
