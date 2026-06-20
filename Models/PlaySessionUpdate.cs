using System;

namespace GameLauncher.Models
{
    public readonly record struct PlaySessionUpdate(string GameId, int PlayTimeSeconds, DateTime LastPlayed);
}
