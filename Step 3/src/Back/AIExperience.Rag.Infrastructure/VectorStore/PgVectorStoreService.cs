using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AIExperience.Rag.Infrastructure.VectorStore;

/// <summary>
/// Implémentation de <see cref="IVectorStoreService"/> utilisant pgvector via Npgsql.
/// Effectue les recherches de similarité cosinus directement en SQL pour les meilleures performances.
/// </summary>
public sealed class PgVectorStoreService : IVectorStoreService
{
    private readonly AppDbContext context;

    public PgVectorStoreService(AppDbContext context)
    {
        this.context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchAsync(
        float[] vector,
        int topK = 20,
        Guid[]? documentIds = null,
        double scoreThreshold = 0.3,
        CancellationToken ct = default)
    {
        var vectorParam = new NpgsqlParameter("vector", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real)
        {
            Value = vector
        };

        // Utilisation du SQL en chaine de caractère car EF Core ne supporte pas les opérations de distance vectorielle.
        // La clause documentFilter est conditionnelle pour éviter d'ajouter une clause inutile si documentIds est null ou vide.
        var documentFilter = documentIds?.Length > 0
            ? "AND dc.document_id = ANY(@docIds)"
            : string.Empty;

        // JOIN documents pour récupérer le nom du fichier source sans requête N+1.
        // start_time_seconds et end_time_seconds ajoutés en fin de SELECT (ordinaux 10 et 11)
        // pour ne pas décaler les ordinaux existants.
        var sql = $"""
            SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
                   dc.section_title, dc.embedding_dimensions, dc.created_at,
                   1 - (dc.embedding <=> @vector::vector) AS score,
                   d.file_name,
                   dc.start_time_seconds, dc.end_time_seconds
            FROM document_chunks dc
            JOIN documents d ON d.id = dc.document_id
            WHERE 1 - (dc.embedding <=> @vector::vector) >= @threshold
            {documentFilter}
            ORDER BY score DESC
            LIMIT @topK
            """;

        var parameters = new List<object> { vectorParam,
            new NpgsqlParameter("threshold", scoreThreshold),
            new NpgsqlParameter("topK", topK)
        };

        if (documentIds?.Length > 0)
            parameters.Add(new NpgsqlParameter("docIds", documentIds));

        var results = new List<(DocumentChunk, double)>();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.Add(p);

        await context.Database.OpenConnectionAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var chunk = DocumentChunk.Create(
                documentId: reader.GetGuid(1),
                content: reader.GetString(2),
                chunkIndex: reader.GetInt32(3),
                embeddingDimensions: reader.GetInt32(6),
                pageNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                sectionTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
                documentName: reader.IsDBNull(9) ? null : reader.GetString(9),
                startTime: reader.IsDBNull(10) ? null : TimeSpan.FromSeconds(reader.GetDouble(10)),
                endTime: reader.IsDBNull(11) ? null : TimeSpan.FromSeconds(reader.GetDouble(11)));

            var score = reader.GetDouble(8);
            results.Add((chunk, score));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, double Score)>> SearchFullTextAsync(
        string query,
        int topK = 10,
        Guid[]? documentIds = null,
        CancellationToken ct = default)
    {
        var documentFilter = documentIds?.Length > 0
            ? "AND dc.document_id = ANY(@docIds)"
            : string.Empty;

        // plainto_tsquery convertit la phrase en opérateurs AND implicites — plus robuste que to_tsquery.
        // ts_rank retourne un score flottant entre 0 et 1 selon la fréquence et position des termes.
        // JOIN documents pour récupérer le nom du fichier source sans requête N+1.
        // start_time_seconds et end_time_seconds ajoutés en fin de SELECT (ordinaux 10 et 11).
        var sql = $"""
            SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
                   dc.section_title, dc.embedding_dimensions, dc.created_at,
                   ts_rank(to_tsvector('french', dc.content), plainto_tsquery('french', @query))::float8 AS score,
                   d.file_name,
                   dc.start_time_seconds, dc.end_time_seconds
            FROM document_chunks dc
            JOIN documents d ON d.id = dc.document_id
            WHERE to_tsvector('french', dc.content) @@ plainto_tsquery('french', @query)
            {documentFilter}
            ORDER BY score DESC
            LIMIT @topK
            """;

        var parameters = new List<object>
        {
            new NpgsqlParameter("query", query),
            new NpgsqlParameter("topK", topK)
        };

        if (documentIds?.Length > 0)
            parameters.Add(new NpgsqlParameter("docIds", documentIds));

        var results = new List<(DocumentChunk, double)>();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.Add(p);

        await context.Database.OpenConnectionAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var chunk = DocumentChunk.Create(
                documentId: reader.GetGuid(1),
                content: reader.GetString(2),
                chunkIndex: reader.GetInt32(3),
                embeddingDimensions: reader.GetInt32(6),
                pageNumber: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                sectionTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
                documentName: reader.IsDBNull(9) ? null : reader.GetString(9),
                startTime: reader.IsDBNull(10) ? null : TimeSpan.FromSeconds(reader.GetDouble(10)),
                endTime: reader.IsDBNull(11) ? null : TimeSpan.FromSeconds(reader.GetDouble(11)));

            results.Add((chunk, reader.GetDouble(8)));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(DocumentChunk chunk, float[] embedding, CancellationToken ct = default)
    {
        var sql = """
            INSERT INTO document_chunks
                ("id", document_id, content, chunk_index, page_number, section_title,
                 embedding_dimensions, embedding, created_at, start_time_seconds, end_time_seconds)
            VALUES
                (@id, @documentId, @content, @chunkIndex, @pageNumber, @sectionTitle,
                 @embDims, @embedding::vector, NOW(), @startTimeSecs, @endTimeSecs)
            ON CONFLICT ("id") DO UPDATE SET
                content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                start_time_seconds = EXCLUDED.start_time_seconds,
                end_time_seconds = EXCLUDED.end_time_seconds;
            """;

        await context.Database.ExecuteSqlRawAsync(sql,
            new NpgsqlParameter("id", chunk.Id),
            new NpgsqlParameter("documentId", chunk.DocumentId),
            new NpgsqlParameter("content", chunk.Content),
            new NpgsqlParameter("chunkIndex", chunk.ChunkIndex),
            new NpgsqlParameter("pageNumber", (object?)chunk.PageNumber ?? DBNull.Value),
            new NpgsqlParameter("sectionTitle", (object?)chunk.SectionTitle ?? DBNull.Value),
            new NpgsqlParameter("embDims", chunk.EmbeddingDimensions),
            new NpgsqlParameter("embedding", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real) { Value = embedding },
            new NpgsqlParameter("startTimeSecs", (object?)(chunk.StartTime?.TotalSeconds) ?? DBNull.Value),
            new NpgsqlParameter("endTimeSecs", (object?)(chunk.EndTime?.TotalSeconds) ?? DBNull.Value));
    }

    /// <inheritdoc/>
    public async Task UpsertBatchAsync(
        IReadOnlyList<(DocumentChunk Chunk, float[] Embedding)> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        // Transaction unique : tous les INSERTs partagent un seul commit PostgreSQL.
        // Réduit les aller-retours réseau de N à 1 comparé à N appels UpsertAsync séquentiels.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        foreach (var (chunk, embedding) in items)
            await UpsertAsync(chunk, embedding, ct);

        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken ct = default)
        => await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM document_chunks WHERE document_id = @docId",
            new NpgsqlParameter("docId", documentId));
}
