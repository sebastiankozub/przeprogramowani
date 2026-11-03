namespace MovieDate.Data;

/// <summary>
/// One message between a pair that passes the conversation gate (matched and friends in either direction).
/// Both sides are AspNetUsers.Id, never the email.
/// </summary>
public class Message
{
    public const int BodyMaxLength = 2000;

    public long Id { get; set; }

    public required string SenderId { get; set; }

    public required string RecipientId { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
