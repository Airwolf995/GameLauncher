using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher.Tests;

public sealed class LibraryFavoriteFilterTests
{
    [Fact]
    public void Build_BietetFavoritenAlsFilterAn()
    {
        var options = LibraryFilterOptionsBuilder.Build(LocalizationService.Instance, []);

        Assert.Contains(options, option => option.Key == Constants.Filters.Favorites);
    }

    /// <summary>
    /// Die Auswahlliste und die Filterung dürfen nicht auseinanderlaufen: Jede
    /// angebotene Option muss auch ausgewertet werden.
    /// </summary>
    [Fact]
    public void MatchesFilter_WertetDenAngebotenenFavoritenfilterAus()
    {
        var favorit = new Game { Id = "g1", Name = "Favorit", IsFavorite = true };
        var anderes = new Game { Id = "g2", Name = "Anderes", IsFavorite = false };

        Assert.True(LibraryViewSnapshotBuilder.MatchesFilter(favorit, "", Constants.Filters.Favorites));
        Assert.False(LibraryViewSnapshotBuilder.MatchesFilter(anderes, "", Constants.Filters.Favorites));
    }

    [Fact]
    public void MatchesFilter_UebergehtAusgeblendeteFavoriten()
    {
        var game = new Game { Id = "g3", Name = "Verstecktes Lieblingsspiel", IsFavorite = true, IsHidden = true };

        Assert.False(LibraryViewSnapshotBuilder.MatchesFilter(game, "", Constants.Filters.Favorites));
    }

    [Fact]
    public void NormalizeFilterKey_ErkenntDenGespeichertenFavoritenfilter()
    {
        Assert.Equal(Constants.Filters.Favorites, LibraryFilterService.NormalizeFilterKey("Favoriten"));
        Assert.Equal(Constants.Filters.Favorites, LibraryFilterService.NormalizeFilterKey("favorites"));
    }
}
