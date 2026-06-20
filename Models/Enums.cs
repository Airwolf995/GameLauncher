namespace GameLauncher.Models
{
    /// <summary>Card size options for the game library grid.</summary>
    public enum CardSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>View mode for the game library display.</summary>
    public enum ViewMode
    {
        Cards,
        List
    }

    /// <summary>Sort modes for the game library.</summary>
    public enum GameSortMode
    {
        Name,
        Favorites,
        LastPlayed,
        PlayTime
    }
}
