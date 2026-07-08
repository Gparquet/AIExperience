using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;


/// <summary>
/// Configuration EF Core pour l'entité <see cref="DocumentChunk"/>.
/// Inclut la colonne vectorielle pgvector avec index HNSW pour la recherche sémantique.
/// </summary>
/// 
public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");


        builder.Property(c => c.DocumentId)
            .HasColumnName("document_id");

        builder.Property(c => c.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(c => c.ChunkIndex)
            .HasColumnName("chunk_index");

        builder.Property(c => c.PageNumber)
            .HasColumnName("page_number");

        builder.Property(c => c.SectionTitle)
            .HasColumnName("section_title")
            .HasMaxLength(500);

        builder.Property(c => c.EmbeddingDimensions)
            .HasColumnName("embedding_dimensions");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at");

        var vectorConverter = new ValueConverter<float[], Vector>(
            v => new Vector(v),
            v => v.ToArray()
        );

        builder.Property<float[]>("Embedding")
            .HasColumnName("embedding")
            .HasColumnType("vector(768)")
            .HasConversion(vectorConverter);

        builder.Property(c => c.StartTime)
            .HasColumnName("start_time_seconds")
            .HasConversion(
                v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
                v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);

        builder.Property(c => c.EndTime)
            .HasColumnName("end_time_seconds")
            .HasConversion(
                v => v.HasValue ? v.Value.TotalSeconds : (double?)null,
                v => v.HasValue ? TimeSpan.FromSeconds(v.Value) : null);

        builder.HasIndex(c => c.DocumentId);
        builder.HasIndex(c => c.ChunkIndex);

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
