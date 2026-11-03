using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MovieDate.Tests;

/// <summary>
/// Friends through the API: a user can add only someone from their own match list, and the list shows both
/// directions. Match rows are seeded directly; the background recompute worker is off for this collection so no
/// recompute adds a match the test did not seed.
/// </summary>
[Collection(PostgresAppWithoutRecomputeWorkerCollection.Name)]
public sealed class FriendsTests(PostgresAppWithoutRecomputeWorkerFixture factory)
{
    private record CityBody(int Id, string Name, string Voivodeship);

    private record FriendBody(string UserId, string DisplayName, CityBody City);

    private record FriendsBody(List<FriendBody> Friends, List<FriendBody> AddedMe);

    private record MatchBody(string UserId, bool IsFriend);

    [Fact]
    public async Task Add_friend_from_own_match_list_returns_201_and_lists_both_directions()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Przyjaciel A", "Przyjaciel B");
        using var disposeA = a;
        using var disposeB = b;

        var response = await a.Client.PostAsJsonAsync("/api/friends", new { userId = b.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", json);
        var added = JsonSerializer.Deserialize<FriendBody>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(b.Id, added.UserId);
        Assert.Equal("Przyjaciel B", added.DisplayName);
        Assert.Equal("Kraków", added.City.Name);

        var aFriends = await Friends(a);
        Assert.Equal(b.Id, Assert.Single(aFriends.Friends).UserId);
        Assert.Empty(aFriends.AddedMe);

        var bFriends = await Friends(b);
        Assert.Empty(bFriends.Friends);
        Assert.Equal(a.Id, Assert.Single(bFriends.AddedMe).UserId);

        // Once B adds A back, A moves from B's AddedMe to B's Friends.
        await TestPairs.AddFriend(b, a);
        bFriends = await Friends(b);
        Assert.Equal(a.Id, Assert.Single(bFriends.Friends).UserId);
        Assert.Empty(bFriends.AddedMe);
    }

    [Fact]
    public async Task Add_friend_not_on_match_list_returns_404()
    {
        using var a = await TestPairs.NewUser(factory, "Samotny A");
        using var stranger = await TestPairs.NewUser(factory, "Obcy");

        foreach (var userId in new[] { stranger.Id, a.Id, Guid.NewGuid().ToString() })
        {
            var response = await a.Client.PostAsJsonAsync("/api/friends", new { userId });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var friends = await Friends(a);
        Assert.Empty(friends.Friends);
        Assert.Empty((await Friends(stranger)).AddedMe);
    }

    [Fact]
    public async Task Add_friend_twice_returns_409()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Dwa razy A", "Dwa razy B");
        using var disposeA = a;
        using var disposeB = b;
        await TestPairs.AddFriend(a, b);

        var response = await a.Client.PostAsJsonAsync("/api/friends", new { userId = b.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("already_friend", problem.RootElement.GetProperty("title").GetString());
        Assert.Single((await Friends(a)).Friends);
    }

    [Fact]
    public async Task Matches_list_marks_friend_with_IsFriend()
    {
        var (a, b) = await TestPairs.NewMatchedPair(factory, "Flaga A", "Flaga B");
        using var disposeA = a;
        using var disposeB = b;
        using var c = await TestPairs.NewUser(factory, "Flaga C");
        await TestPairs.SeedMatch(factory, a, c);

        await TestPairs.AddFriend(a, b);

        var aList = await Matches(a);
        Assert.True(Assert.Single(aList, m => m.UserId == b.Id).IsFriend);
        Assert.False(Assert.Single(aList, m => m.UserId == c.Id).IsFriend);

        // Caller's own direction only: B has not added A.
        Assert.False(Assert.Single(await Matches(b), m => m.UserId == a.Id).IsFriend);
    }

    private static async Task<FriendsBody> Friends(PairUser user)
    {
        var response = await user.Client.GetAsync("/api/friends");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", json);
        return JsonSerializer.Deserialize<FriendsBody>(json, JsonSerializerOptions.Web)!;
    }

    private static async Task<List<MatchBody>> Matches(PairUser user)
    {
        var response = await user.Client.GetAsync("/api/matches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<MatchBody>>() ?? [];
    }
}
