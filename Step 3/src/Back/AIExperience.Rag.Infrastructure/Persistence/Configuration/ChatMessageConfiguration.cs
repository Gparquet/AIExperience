using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="ChatMessage"/>.
/// </summary>
public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.SessionId).HasColumnName("session_id");
        builder.Property(m => m.Role).HasColumnName("role").HasConversion<string>();
        builder.Property(m => m.Content).HasColumnName("content").IsRequired();
        builder.Property(m => m.TokensUsed).HasColumnName("tokens_used");
        builder.Property(m => m.StrategyUsed).HasColumnName("strategy_used").HasConversion<string>();
        builder.Property(m => m.DurationMs).HasColumnName("duration_ms");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Citations)
            .WithOne(c => c.Message)
            .HasForeignKey(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.SessionId);
        builder.HasIndex(m => m.CreatedAt);
    }
}
