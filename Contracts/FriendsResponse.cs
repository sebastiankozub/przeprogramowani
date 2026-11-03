namespace MovieDate.Contracts;

/// <summary>
/// <see cref="Friends"/>: people the caller added. <see cref="AddedMe"/>: people who added the caller and whom
/// the caller has not added. Both ordered by display name.
/// </summary>
public record FriendsResponse(IReadOnlyList<FriendResponse> Friends, IReadOnlyList<FriendResponse> AddedMe);
