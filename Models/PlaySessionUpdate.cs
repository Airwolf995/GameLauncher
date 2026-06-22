using System;

namespace GameLauncher.Models
{
    public readonly record struct PlaySessionUpdate(string GameId, string GameName, int PlayTimeSeconds, DateTime LastPlayed);
}
