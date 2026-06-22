using System.Text.Json.Serialization;

namespace GameLauncher.Models
{
    public class PlayTimeEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("seconds")]
        public int Seconds { get; set; }
    }
}
