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

                using var entryDocument = JsonDocument.ParseValue(ref reader);
                if (TryReadEntry(entryDocument.RootElement, options, out var entry))
                {
                    result[gameId] = entry;
                }
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

        private static bool TryReadEntry(
            JsonElement element,
            JsonSerializerOptions options,
            out PlayTimeEntry entry)
        {
            entry = new PlayTimeEntry();

            if (element.ValueKind == JsonValueKind.Number)
            {
                if (!element.TryGetInt64(out var legacySeconds) ||
                    legacySeconds < 0 ||
                    legacySeconds > int.MaxValue)
                {
                    return false;
                }

                entry.Seconds = (int)legacySeconds;
                return true;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            try
            {
                var parsedEntry = element.Deserialize<PlayTimeEntry>(options);
                if (parsedEntry == null || parsedEntry.Seconds < 0)
                {
                    return false;
                }

                entry = parsedEntry;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
