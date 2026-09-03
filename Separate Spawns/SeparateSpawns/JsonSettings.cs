using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class JsonSettings
    {
        public static readonly JsonSerializerSettings Serializer = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new Vector3Converter() }
        };

        public static readonly JsonSerializerSettings Compact = new JsonSerializerSettings
        {
            Converters = { new Vector3Converter() }
        };

        private sealed class Vector3Converter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3) || objectType == typeof(Vector3?);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                var vector = (Vector3)value;
                writer.WriteStartObject();
                writer.WritePropertyName("x");
                writer.WriteValue(vector.x);
                writer.WritePropertyName("y");
                writer.WriteValue(vector.y);
                writer.WritePropertyName("z");
                writer.WriteValue(vector.z);
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return objectType == typeof(Vector3?) ? null : Vector3.zero;
                }

                var obj = JObject.Load(reader);
                var vector = new Vector3(
                    obj.Value<float>("x"),
                    obj.Value<float>("y"),
                    obj.Value<float>("z"));

                if (objectType == typeof(Vector3?))
                {
                    return (Vector3?)vector;
                }

                return vector;
            }
        }
    }
}
