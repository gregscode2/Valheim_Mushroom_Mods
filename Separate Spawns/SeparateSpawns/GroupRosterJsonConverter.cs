using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeparateSpawns
{
    internal sealed class GroupRosterJsonConverter : JsonConverter<GroupRoster>
    {
        public override void WriteJson(JsonWriter writer, GroupRoster value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("groups");
            writer.WriteStartObject();

            if (value?.Groups != null)
            {
                foreach (var pair in value.Groups)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteGroupEntry(writer, pair.Value);
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        public override GroupRoster ReadJson(JsonReader reader, Type objectType, GroupRoster existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return GroupRoster.CreateEmpty();
            }

            var root = JObject.Load(reader);
            var groupsToken = root["groups"] ?? root["Groups"];
            var roster = existingValue ?? GroupRoster.CreateEmpty();
            roster.Groups ??= new Dictionary<string, GroupEntry>();

            if (groupsToken is JObject groupsObject)
            {
                foreach (var property in groupsObject.Properties())
                {
                    roster.Groups[property.Name] = ReadGroupEntry(property.Value);
                }
            }

            return roster;
        }

        private static void WriteGroupEntry(JsonWriter writer, GroupEntry entry)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("players");
            writer.WriteStartArray();
            if (entry?.Players != null)
            {
                foreach (var player in entry.Players)
                {
                    writer.WriteValue(player);
                }
            }

            writer.WriteEndArray();

            if (entry != null && entry.HasDifficulty)
            {
                writer.WritePropertyName("difficulty");
                writer.WriteValue(entry.Difficulty.Value);
            }

            writer.WriteEndObject();
        }

        private static GroupEntry ReadGroupEntry(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new GroupEntry();
            }

            // Legacy: "groupA": ["Steam_..."]
            if (token.Type == JTokenType.Array)
            {
                return GroupEntry.FromPlayers(ReadPlayerList(token));
            }

            // Convenience: "groupA": "Steam_..."
            if (token.Type == JTokenType.String)
            {
                return GroupEntry.FromPlayers(ReadPlayerList(token));
            }

            if (token is JObject obj)
            {
                var playersToken = obj["players"] ?? obj["Players"];
                var difficultyToken = obj["difficulty"] ?? obj["Difficulty"];
                return new GroupEntry
                {
                    Players = ReadPlayerList(playersToken),
                    Difficulty = difficultyToken?.Type == JTokenType.Integer ? difficultyToken.Value<int?>() : null
                };
            }

            return new GroupEntry();
        }

        private static List<string> ReadPlayerList(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string>();
            }

            // Preferred: "players": ["Steam_..."]
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<string>>() ?? new List<string>();
            }

            // Convenience: "players": "Steam_..."
            if (token.Type == JTokenType.String)
            {
                var id = token.Value<string>();
                return string.IsNullOrWhiteSpace(id)
                    ? new List<string>()
                    : new List<string> { id.Trim() };
            }

            return new List<string>();
        }
    }
}
