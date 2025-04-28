using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NetVisionLibrary
{
    public class RecordConverter : JsonConverter<Record>
    {
        public override Record ReadJson(JsonReader reader, Type objectType, Record? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);
            var record = existingValue ?? new Record(); // Use existingValue if provided, else create new

            // Check if the JSON object has a "Properties" field
            if (obj["Properties"] != null)
            {
                // Deserialize the "Properties" object as a dictionary
                var properties = new Dictionary<string, string>();
                foreach (var prop in obj["Properties"]!)
                {
                    string key = prop.Path.Split('.').Last();
                    string value = prop.First?.ToString() ?? string.Empty; // Handle null values
                    properties[key] = value;
                }
                record.Properties = properties;
                record.ColumnOrder = properties.Keys.ToList();
            }
            else
            {
                // Treat the entire object as the properties (flat structure)
                var properties = new Dictionary<string, string>();
                foreach (var prop in obj)
                {
                    properties[prop.Key] = prop.Value?.ToString() ?? string.Empty; // Handle null values
                }
                record.Properties = properties;
                record.ColumnOrder = properties.Keys.ToList();
            }

            return record;
        }

        public override void WriteJson(JsonWriter writer, Record? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();

            // Write properties in the order specified by ColumnOrder, without a "Properties" wrapper
            foreach (var key in value.ColumnOrder)
            {
                if (value.Properties.ContainsKey(key))
                {
                    writer.WritePropertyName(key);
                    writer.WriteValue(value.Properties[key]);
                }
            }

            writer.WriteEndObject();
        }
    }
}