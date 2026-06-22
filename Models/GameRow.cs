using System.Collections.Generic;

namespace GameLauncher.Models
{
    public sealed class GameRow
    {
        public GameRow(IReadOnlyList<Game> games)
        {
            Games = games;
        }

        public IReadOnlyList<Game> Games { get; }
    }
}
