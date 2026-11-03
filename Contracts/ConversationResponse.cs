namespace MovieDate.Contracts;

/// <summary>The other side of the pair and the latest messages of the pair (both directions), oldest first.</summary>
public record ConversationResponse(FriendResponse With, IReadOnlyList<MessageResponse> Messages);
