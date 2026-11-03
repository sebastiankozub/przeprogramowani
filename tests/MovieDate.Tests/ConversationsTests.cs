using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

/// <summary>
/// The conversation gate: a pair may read and send only when matched and friends in at least one direction;
/// anyone else gets 404, checked before body validation. Match rows are seeded directly; the background
/// recompute worker is off for this collection, so a "stranger" can never be silently matched by a top-up.
/// </summary>
[Collection(PostgresAppWithoutRecomputeWorkerCollection.Name)]
public sealed class ConversationsTests(PostgresAppWithoutRecomputeWorkerFixture factory)
{
    private record CityBody(int Id, string Name, string Voivodeship);

    private record FriendBody(string UserId, string DisplayName, CityBody City);

    private record MessageBody(long Id, bool FromMe, string Body, DateTimeOffset SentAt);

    private record ConversationBody(FriendBody With, List<MessageBody> Messages);

    [Fact]
    public async Task Friend_pair_can_send_and_read_messages_oldest_first()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Pisze A", "Pisze B");
        using var disposeA = a;
        using var disposeB = b;
        await TestPairs.AddFriend(a, b);

        var first = await Send(a, b, "  Cześć!  ");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var sent = (await first.Content.ReadFromJsonAsync<MessageBody>())!;
        Assert.True(sent.FromMe);
        Assert.Equal("Cześć!", sent.Body);

        Assert.Equal(HttpStatusCode.Created, (await Send(a, b, "Idziemy do kina?")).StatusCode);

        var conversation = await Read(a, b);
        Assert.Equal(b.Id, conversation.With.UserId);
        Assert.Equal("Pisze B", conversation.With.DisplayName);
        Assert.Equal(["Cześć!", "Idziemy do kina?"], conversation.Messages.Select(m => m.Body));
        Assert.All(conversation.Messages, m => Assert.True(m.FromMe));
        Assert.Equal(sent.Id, conversation.Messages[0].Id);
    }

    [Fact]
    public async Task Recipient_who_did_not_add_sender_can_read_and_reply()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Dodał A", "Nie dodał B");
        using var disposeA = a;
        using var disposeB = b;
        await TestPairs.AddFriend(a, b);

        Assert.Equal(HttpStatusCode.Created, (await Send(a, b, "Hej")).StatusCode);

        var bView = await Read(b, a);
        Assert.Equal(a.Id, bView.With.UserId);
        var received = Assert.Single(bView.Messages);
        Assert.False(received.FromMe);
        Assert.Equal("Hej", received.Body);

        Assert.Equal(HttpStatusCode.Created, (await Send(b, a, "No hej")).StatusCode);

        var aView = await Read(a, b);
        Assert.Equal([(true, "Hej"), (false, "No hej")], aView.Messages.Select(m => (m.FromMe, m.Body)));
    }

    [Fact]
    public async Task Stranger_cannot_read_or_send_to_pair_returns_404()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Para A", "Para B");
        using var disposeA = a;
        using var disposeB = b;
        using var stranger = await TestPairs.NewUser(factory, "Nieznajomy");
        await TestPairs.AddFriend(a, b);
        Assert.Equal(HttpStatusCode.Created, (await Send(a, b, "Tylko dla B")).StatusCode);

        foreach (var target in new[] { a, b })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/conversations/{target.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(stranger, target, "Cześć")).StatusCode);
        }

        // Nothing the stranger tried landed in the pair's conversation.
        Assert.Equal(["Tylko dla B"], (await Read(b, a)).Messages.Select(m => m.Body));
    }

    [Fact]
    public async Task Own_id_conversation_returns_404()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Sam A", "Sam B");
        using var disposeA = a;
        using var disposeB = b;
        await TestPairs.AddFriend(a, b);

        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"/api/conversations/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(a, a, "Do siebie")).StatusCode);
    }

    [Fact]
    public async Task Matched_without_friendship_cannot_read_or_send_returns_404()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Bez przyjaźni A", "Bez przyjaźni B");
        using var disposeA = a;
        using var disposeB = b;

        foreach (var (from, to) in new[] { (a, b), (b, a) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await from.Client.GetAsync($"/api/conversations/{to.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(from, to, "Cześć")).StatusCode);
        }
    }

    [Fact]
    public async Task Empty_or_too_long_body_returns_400_for_pair_but_404_for_stranger()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Treść A", "Treść B");
        using var disposeA = a;
        using var disposeB = b;
        using var stranger = await TestPairs.NewUser(factory, "Treść obcy");
        await TestPairs.AddFriend(a, b);
        var tooLong = new string('x', Message.BodyMaxLength + 1);

        foreach (var body in new[] { "", "   ", tooLong })
        {
            var response = await Send(a, b, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("body", out _));

            Assert.Equal(HttpStatusCode.NotFound, (await Send(stranger, b, body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Created, (await Send(a, b, new string('x', Message.BodyMaxLength))).StatusCode);
        Assert.Single((await Read(a, b)).Messages);
    }

    [Fact]
    public async Task Conversation_returns_latest_100_messages()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Gaduła A", "Gaduła B");
        using var disposeA = a;
        using var disposeB = b;
        await TestPairs.AddFriend(a, b);

        const int total = 105;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTimeOffset.UtcNow.AddHours(-1);
            for (var i = 1; i <= total; i++)
            {
                // Alternate directions so the read must merge both sides of the pair.
                var (sender, recipient) = i % 2 == 0 ? (b, a) : (a, b);
                db.Messages.Add(new Message
                {
                    SenderId = sender.Id,
                    RecipientId = recipient.Id,
                    Body = $"m{i}",
                    SentAt = start.AddSeconds(i),
                });
            }

            await db.SaveChangesAsync();
        }

        var conversation = await Read(a, b);

        Assert.Equal(100, conversation.Messages.Count);
        Assert.Equal(Enumerable.Range(total - 99, 100).Select(i => $"m{i}"), conversation.Messages.Select(m => m.Body));
        Assert.False(conversation.Messages[0].FromMe); // m6 (even) was sent by B
        Assert.True(conversation.Messages[^1].FromMe); // m105 (odd) was sent by A
    }

    private static Task<HttpResponseMessage> Send(PairUser from, PairUser to, string body) =>
        from.Client.PostAsJsonAsync($"/api/conversations/{to.Id}/messages", new { body });

    private static async Task<ConversationBody> Read(PairUser me, PairUser other)
    {
        var response = await me.Client.GetAsync($"/api/conversations/{other.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", json);
        return JsonSerializer.Deserialize<ConversationBody>(json, JsonSerializerOptions.Web)!;
    }
}
