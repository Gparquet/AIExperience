using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AIExperience.Rag.Infrastructure.Persistence.Repositories;


/// <summary>
/// Implémentation du repository document <see cref="IDocumentRepository"/>.
/// </summary>
public sealed class DocumentRepository(AppDbContext context) : IDocumentRepository
{
    /// <inheritdoc/>
    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Documents
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc/>
    public async Task<IEnumerable<Document>> GetAllAsync(CancellationToken ct = default)
        => await context.Documents
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<(IEnumerable<Document> Items, int TotalCount)> GetByUserIdAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = context.Documents
            .Include(d => d.Chunks)
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Document>> GetPendingDocumentsAsync(int maxCount = 10, CancellationToken ct = default)
        => await context.Documents
            .Where(d => d.Status == IngestionStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .Take(maxCount)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task AddAsync(Document document, CancellationToken ct = default)
        => await context.Documents.AddAsync(document, ct);

    /// <inheritdoc/>
    public Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        context.Documents.Update(document);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var document = await context.Documents.FindAsync([id], ct);
        if (document is not null)
            context.Documents.Remove(document);
    }
}
