using AIExperience.Rag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace AIExperience.Rag.Infrastructure.Persistence.Configuration;


/// <summary>
/// Configuration EF Core pour l'entité <see cref="Document"/>.
/// </summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(d => d.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.FileReference).HasColumnName("file_reference").HasMaxLength(500);
        builder.Property(d => d.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(d => d.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(d => d.ChunkingStrategy).HasColumnName("chunking_strategy").HasConversion<string>();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.FileSizeBytes).HasColumnName("file_size_bytes");

        builder.OwnsOne(d => d.Metadata, metadata =>
        {
            metadata.Property(m => m.Title).HasColumnName("metadata_title").HasMaxLength(500);
            metadata.Property(m => m.Author).HasColumnName("metadata_author").HasMaxLength(255);
            metadata.Property(m => m.PageCount).HasColumnName("metadata_page_count");
            metadata.Property(m => m.Language).HasColumnName("metadata_language").HasMaxLength(10);
            metadata.Property(m => m.Tags)
                .HasColumnName("metadata_tags")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                    v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default)!.AsReadOnly())
                .HasColumnType("jsonb");
        });

        builder.HasMany(d => d.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.CreatedAt);
    }
}
