using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="ConversationSession"/>.
/// </summary>
public sealed class ConversationSessionConfiguration : IEntityTypeConfiguration<ConversationSession>
{
    public void Configure(EntityTypeBuilder<ConversationSession> builder)
    {
        builder.ToTable("conversation_sessions");
        builder.HasKey(s => s.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(s => s.UserId).HasMaxLength(255).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.Title).HasMaxLength(500).HasColumnName("title").IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.UpdatedAt);
    }
}
