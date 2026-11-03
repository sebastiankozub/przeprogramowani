namespace MovieDate.Contracts;

public record MessageResponse(long Id, bool FromMe, string Body, DateTimeOffset SentAt);
