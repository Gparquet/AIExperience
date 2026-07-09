using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="OutboxMessage"/>, mappée sur la table
/// <c>outbox_messages</c> déjà présente au schéma SQL.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(500).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        builder.Property(m => m.Error).HasColumnName("error").HasMaxLength(2000);
        builder.Property(m => m.RetryCount).HasColumnName("retry_count");

        builder.HasIndex(m => m.ProcessedAt);
        builder.HasIndex(m => m.CreatedAt);
    }
}
