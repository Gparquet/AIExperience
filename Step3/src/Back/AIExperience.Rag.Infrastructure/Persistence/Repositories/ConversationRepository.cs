using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Models;
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
                .ThenInclude(m => m.Citations)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    /// <inheritdoc/>
    public async Task<IEnumerable<ConversationSession>> GetSessionsByUserIdAsync(string userId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Where(s => s.UserId == userId)
            .Include(s => s.Messages)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConversationSessionSummary>> GetSessionSummariesAsync(string userId, CancellationToken ct = default)
        => await context.ConversationSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            // Projection SQL directe : le COUNT des messages est calculé côté base, sans matérialiser les messages.
            .Select(s => new ConversationSessionSummary(s.Id, s.Title, s.UpdatedAt, s.Messages.Count))
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

    /// <inheritdoc/>
    public async Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await context.ConversationSessions.FindAsync([sessionId], ct);
        if (session is null)
            return false;

        context.ConversationSessions.Remove(session);
        return true;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAllSessionsAsync(string userId, CancellationToken ct = default)
    {
        var sessions = await context.ConversationSessions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        context.ConversationSessions.RemoveRange(sessions);
        return sessions.Count;
    }
}
