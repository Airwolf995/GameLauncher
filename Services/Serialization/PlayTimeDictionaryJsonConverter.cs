using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameLauncher.Models;

namespace GameLauncher.Services.Serialization
{
    public sealed class PlayTimeDictionaryJsonConverter : JsonConverter<Dictionary<string, PlayTimeEntry>>
    {
        public override Dictionary<string, PlayTimeEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Für Spielzeitdaten wurde ein Objekt erwartet.");
            }

            var result = new Dictionary<string, PlayTimeEntry>(StringComparer.Ordinal);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return result;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Ungültiger Spielzeit-Eintrag in der Konfiguration.");
                }

                var gameId = reader.GetString() ?? string.Empty;

                if (!reader.Read())
                {
                    throw new JsonException("Unvollständiger Spielzeit-Eintrag in der Konfiguration.");
                }

                result[gameId] = ReadEntry(ref reader, options);
            }

            throw new JsonException("Spielzeitdaten wurden nicht korrekt beendet.");
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, PlayTimeEntry> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            foreach (var pair in value)
            {
                writer.WritePropertyName(pair.Key);
                JsonSerializer.Serialize(writer, pair.Value ?? new PlayTimeEntry(), options);
            }

            writer.WriteEndObject();
        }

        private static PlayTimeEntry ReadEntry(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => new PlayTimeEntry
                {
                    Seconds = reader.GetInt32()
                },
                JsonTokenType.StartObject => JsonSerializer.Deserialize<PlayTimeEntry>(ref reader, options) ?? new PlayTimeEntry(),
                _ => throw new JsonException("Spielzeit-Einträge müssen eine Zahl oder ein Objekt sein.")
            };
        }
    }
}
