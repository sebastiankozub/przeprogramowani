namespace MovieDate.Data;

/// <summary>
/// One-sided friendship: <see cref="UserId"/> has added <see cref="FriendId"/> from their own match list.
/// One row per (user, friend); both sides are AspNetUsers.Id, never the email.
/// </summary>
public class Friendship
{
    public required string UserId { get; set; }

    public required string FriendId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
