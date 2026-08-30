namespace GameLauncher.Services
{
    /// <summary>
    /// Ein zur Auswahl angebotenes Titelbild. <paramref name="SourceLabel"/> nennt
    /// die Herkunft und bei Steam-Treffern zusätzlich den gefundenen Spielnamen,
    /// damit bei mehrdeutigen Suchbegriffen erkennbar bleibt, zu welchem Titel das
    /// Bild gehört.
    /// </summary>
    public sealed record CoverCandidate(string ImageUrl, string SourceLabel);
}
