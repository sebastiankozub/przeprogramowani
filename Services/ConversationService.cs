using Microsoft.EntityFrameworkCore;
using MovieDate.Contracts;
using MovieDate.Data;
using Npgsql;

namespace MovieDate.Services;

public enum ConversationOutcome
{
    Success,

    /// <summary>The pair does not pass the gate (not matched, or no friendship in either direction).</summary>
    NotFound,

    /// <summary>The trimmed body is empty or longer than <see cref="Message.BodyMaxLength"/>.</summary>
    InvalidBody,
}

public sealed record SendMessageResult(ConversationOutcome Outcome, MessageResponse? Value = null)
{
    public static SendMessageResult Ok(MessageResponse value) => new(ConversationOutcome.Success, value);

    public static readonly SendMessageResult NotFound = new(ConversationOutcome.NotFound);
    public static readonly SendMessageResult InvalidBody = new(ConversationOutcome.InvalidBody);
}

/// <summary>
/// Messages between two users. A pair may talk only when they are matched and at least one of them has added the
/// other as a friend; to anyone else the conversation does not exist (404). Keyed on AspNetUsers.Id, never the email.
/// </summary>
public sealed class ConversationService(AppDbContext db)
{
    /// <summary>At most this many of the pair's latest messages are returned.</summary>
    public const int MaxMessages = 100;

    /// <summary>The gate: a match row (caller → other) exists AND a friendship exists in either direction.</summary>
    public async Task<bool> CanTalkAsync(string userId, string otherId, CancellationToken ct = default) =>
        userId != otherId
        && await db.Matches.AnyAsync(m => m.UserId == userId && m.CandidateId == otherId, ct)
        && await db.Friendships.AnyAsync(
            f => (f.UserId == userId && f.FriendId == otherId) || (f.UserId == otherId && f.FriendId == userId), ct);

    /// <summary>The other side and the pair's latest <see cref="MaxMessages"/> messages, oldest first; null when gated.</summary>
    public async Task<ConversationResponse?> GetAsync(string userId, string otherId, CancellationToken ct = default)
    {
        if (!await CanTalkAsync(userId, otherId, ct))
        {
            return null;
        }

        var with = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == otherId && u.CityId != null && u.DisplayName != null)
            .Select(FriendsService.ToResponse)
            .FirstOrDefaultAsync(ct);
        if (with is null)
        {
            return null;
        }

        // One query per direction, so each is an ordered range scan on IX_Messages_SenderId_RecipientId_SentAt_Id
        // capped at MaxMessages; an OR of both directions would read and sort the pair's whole history.
        var sent = await LatestFromAsync(userId, otherId, fromMe: true, ct);
        var received = await LatestFromAsync(otherId, userId, fromMe: false, ct);
        var latest = sent.Concat(received)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(MaxMessages)
            .Reverse()
            .ToList();

        return new ConversationResponse(with, latest);
    }

    private Task<List<MessageResponse>> LatestFromAsync(string senderId, string recipientId, bool fromMe,
        CancellationToken ct) =>
        db.Messages
            .AsNoTracking()
            .Where(m => m.SenderId == senderId && m.RecipientId == recipientId)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(MaxMessages)
            .Select(m => new MessageResponse(m.Id, fromMe, m.Body, m.SentAt))
            .ToListAsync(ct);

    /// <summary>Gate first (404), then the trimmed body must be 1–<see cref="Message.BodyMaxLength"/> chars.</summary>
    public async Task<SendMessageResult> SendAsync(string userId, string otherId, string? body,
        CancellationToken ct = default)
    {
        if (!await CanTalkAsync(userId, otherId, ct))
        {
            return SendMessageResult.NotFound;
        }

        var trimmed = body?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > Message.BodyMaxLength)
        {
            return SendMessageResult.InvalidBody;
        }

        var message = new Message { SenderId = userId, RecipientId = otherId, Body = trimmed, SentAt = Clock.Now() };
        db.Messages.Add(message);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Either account was deleted between the gate and the insert.
            db.ChangeTracker.Clear();
            return SendMessageResult.NotFound;
        }

        return SendMessageResult.Ok(new MessageResponse(message.Id, true, message.Body, message.SentAt));
    }
}
