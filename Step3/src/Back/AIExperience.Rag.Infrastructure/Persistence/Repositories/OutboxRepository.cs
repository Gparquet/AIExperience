using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AIExperience.Rag.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implémentation du repository outbox <see cref="IOutboxRepository"/>.
/// </summary>
public sealed class OutboxRepository(AppDbContext context) : IOutboxRepository
{
    /// <inheritdoc/>
    public void Add(OutboxMessage message)
        => context.OutboxMessages.Add(message);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int maxCount, CancellationToken ct = default)
        => await context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(maxCount)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public Task UpdateAsync(OutboxMessage message, CancellationToken ct = default)
    {
        context.OutboxMessages.Update(message);
        return Task.CompletedTask;
    }
}
