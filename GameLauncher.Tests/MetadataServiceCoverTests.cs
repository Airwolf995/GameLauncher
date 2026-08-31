using GameLauncher.Services;

namespace GameLauncher.Tests;

public sealed class MetadataServiceCoverTests
{
    [Fact]
    public void BuildSteamStoreSearchUrl_MaskiertDenSuchbegriff()
    {
        string url = MetadataService.BuildSteamStoreSearchUrl("Half-Life 2 & Co");

        Assert.Contains("term=Half-Life%202%20%26%20Co", url);
    }

    [Fact]
    public void BuildSteamHeaderImageUrl_VerwendetDieAppId()
    {
        Assert.Equal(
            "https://cdn.cloudflare.steamstatic.com/steam/apps/620/header.jpg",
            MetadataService.BuildSteamHeaderImageUrl(620));
    }

    [Fact]
    public void ParseSteamStoreCovers_LiestTrefferMitNamenUndBild()
    {
        const string json = """
        { "total": 2, "items": [
            { "id": 620, "name": "Portal 2" },
            { "id": 400, "name": "Portal" }
        ] }
        """;

        var candidates = MetadataService.ParseSteamStoreCovers(json);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(MetadataService.BuildSteamHeaderImageUrl(620), candidates[0].ImageUrl);
        Assert.Equal("Steam: Portal 2", candidates[0].SourceLabel);
    }

    [Fact]
    public void ParseSteamStoreCovers_UeberspringtEintraegeOhneAppId()
    {
        const string json = """
        { "items": [ { "name": "Ohne Kennung" }, { "id": 620, "name": "Portal 2" } ] }
        """;

        var candidates = MetadataService.ParseSteamStoreCovers(json);

        Assert.Single(candidates);
        Assert.Equal("Steam: Portal 2", candidates[0].SourceLabel);
    }

    [Fact]
    public void ParseSteamStoreCovers_BegrenztDieAnzahlDerTreffer()
    {
        string items = string.Join(",", Enumerable.Range(1, 20).Select(i => $"{{\"id\":{i},\"name\":\"Spiel {i}\"}}"));
        string json = $"{{ \"items\": [{items}] }}";

        var candidates = MetadataService.ParseSteamStoreCovers(json);

        Assert.Equal(6, candidates.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"items\": null }")]
    [InlineData("kein json")]
    public void ParseSteamStoreCovers_LiefertBeiUnbrauchbarerAntwortEineLeereListe(string json)
    {
        Assert.Empty(MetadataService.ParseSteamStoreCovers(json));
    }

    [Fact]
    public void ParseSteamGridDbGrids_LiestDieBildadressen()
    {
        const string json = """
        { "data": [ { "url": "https://example.invalid/a.png" }, { "url": "https://example.invalid/b.png" } ] }
        """;

        var candidates = MetadataService.ParseSteamGridDbGrids(json);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("https://example.invalid/a.png", candidates[0].ImageUrl);
        Assert.Equal("SteamGridDB", candidates[0].SourceLabel);
    }

    [Fact]
    public void ParseSteamGridDbGrids_BegrenztDieAnzahlDerTreffer()
    {
        string grids = string.Join(",", Enumerable.Range(1, 30).Select(i => $"{{\"url\":\"https://example.invalid/{i}.png\"}}"));
        string json = $"{{ \"data\": [{grids}] }}";

        var candidates = MetadataService.ParseSteamGridDbGrids(json);

        Assert.Equal(12, candidates.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"data\": [ { \"url\": \"\" } ] }")]
    [InlineData("kein json")]
    public void ParseSteamGridDbGrids_LiefertBeiUnbrauchbarerAntwortEineLeereListe(string json)
    {
        Assert.Empty(MetadataService.ParseSteamGridDbGrids(json));
    }

    [Fact]
    public async Task GetCoverCandidatesAsync_LiefertOhneNamenKeineTreffer()
    {
        var service = new MetadataService();

        var candidates = await service.GetCoverCandidatesAsync("   ");

        Assert.Empty(candidates);
    }
}
