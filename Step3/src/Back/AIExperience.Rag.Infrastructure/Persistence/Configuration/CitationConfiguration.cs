using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;

/// <summary>
/// Configuration EF Core pour l'entité <see cref="Citation"/>.
/// </summary>
public sealed class CitationConfiguration : IEntityTypeConfiguration<Citation>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Citation> builder)
    {
        builder.ToTable("citations");
        builder.HasKey(c => c.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(c => c.MessageId).HasColumnName("message_id");
        builder.Property(c => c.DocumentId).HasColumnName("document_id");
        builder.Property(c => c.DocumentName).HasColumnName("document_name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Excerpt).HasColumnName("excerpt").IsRequired();
        builder.Property(c => c.PageNumber).HasColumnName("page_number");
        // La colonne score existe dans init.sql mais n'était pas mappée : sans cette ligne, EF génère
        // l'identifiant "Score" (casse préservée, entre guillemets) qui ne correspond pas à la colonne
        // "score" — l'INSERT des citations échouerait silencieusement une fois la persistance branchée.
        builder.Property(c => c.Score).HasColumnName("score");

        builder.HasOne(c => c.Message)
            .WithMany(m => m.Citations)
            .HasForeignKey(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.MessageId);
        builder.HasIndex(c => c.DocumentId);
    }
}
