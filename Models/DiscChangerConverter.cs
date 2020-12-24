using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetaBrainz.Common.Json;
using MetaBrainz.MusicBrainz.DiscId.Standards;

namespace DiscChanger.Models
{
    public class DiscChangerConverter: JsonConverter<DiscChanger>
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(DiscChanger).IsAssignableFrom(typeToConvert);

        public override DiscChanger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }
            Dictionary<string, object> propertyValues = new Dictionary<string, object>();
            DiscChanger discChanger = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if(discChanger==null)
                        throw new JsonException();
                    var discChangerType = discChanger.GetType();
                    foreach (var pv in propertyValues)
                    {
                        discChangerType.GetProperty(pv.Key).SetValue(discChanger, pv.Value);
                    }
                    return discChanger;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName)
                    {
                        case "Type":
                            discChanger = DiscChanger.Create(reader.GetString()); break;
                        case "DiscList":
                            if (discChanger == null)
                                throw new JsonException();
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                Type discType = discChanger.getDiscType();
                                Type discArrayType = discType.MakeArrayType();
                                Disc[] discList = (Disc[])JsonSerializer.Deserialize(ref reader, discArrayType, options);
                                foreach (var d in discList)
                                {
                                    discChanger.setDisc(d.Slot, d);
                                }
                            }
                            break;
                        default:
                            if( reader.TokenType!=JsonTokenType.Null)
                                propertyValues.Add(propertyName, reader.GetObject(options));
                            break;
                    }
                }
            }
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, DiscChanger discChanger, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var p in discChanger.GetType().GetProperties().OrderBy(x => x.MetadataToken))
            {
                var v = p.GetValue(discChanger);
                if (v != null)
                {
                    writer.WritePropertyName(p.Name);
                    JsonSerializer.Serialize(writer, v, options);
                }
            }
            writer.WritePropertyName("DiscList");
            object[] dl = new Disc[discChanger.Discs.Count];
            int i = 0;
            foreach (var kvp in discChanger.Discs)
            {
                dl[i++]=kvp.Value;
            }
            Array.Sort(dl, (x, y) => ((Disc)x).CompareTo((Disc)y));
            JsonSerializer.Serialize(writer, dl, options);
            writer.WriteEndObject();
        }
    }
}
