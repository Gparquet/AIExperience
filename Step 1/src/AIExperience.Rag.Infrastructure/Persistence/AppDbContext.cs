using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace AIExperience.Rag.Infrastructure.Persistence
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
    {
        public DbSet<Document> Documents => Set<Document>();

        /// <summary>Table des chunks vectorisés issus de l'ingestion.</summary>
        public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

        /// <summary>Table des sessions de conversation.</summary>
        public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();

        /// <summary>Table des messages de conversation.</summary>
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

        /// <summary>Table des citations de sources.</summary>
        public DbSet<Citation> Citations => Set<Citation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("vector");
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }
    }
}
