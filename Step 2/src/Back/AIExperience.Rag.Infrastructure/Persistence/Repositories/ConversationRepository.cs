using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AIExperience.Rag.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implémentation du repository conversation <see cref="IConversationRepository"/>.
/// </summary>
public sealed class ConversationRepository(AppDbContext context) : IConversationRepository
{
    /// <inheritdoc/>
    public async Task<ConversationSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    /// <inheritdoc/>
    public async Task<IEnumerable<ConversationSession>> GetSessionsByUserIdAsync(string userId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Where(s => s.UserId == userId)
            .Include(s => s.Messages)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(Guid sessionId, int maxTurns = 20, CancellationToken ct = default)
        => await context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .Include(m => m.Citations)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxTurns)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task AddSessionAsync(ConversationSession session, CancellationToken ct = default)
        => await context.ConversationSessions.AddAsync(session, ct);

    /// <inheritdoc/>
    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
        => await context.ChatMessages.AddAsync(message, ct);

    /// <inheritdoc/>
    public Task UpdateSessionAsync(ConversationSession session, CancellationToken ct = default)
    {
        context.ConversationSessions.Update(session);
        return Task.CompletedTask;
    }
}
