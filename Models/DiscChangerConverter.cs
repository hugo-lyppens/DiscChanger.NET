/*  Copyright 2020 Hugo Lyppens

    DiscChangerConverter.cs is part of DiscChanger.NET.

    DiscChanger.NET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    DiscChanger.NET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with DiscChanger.NET.  If not, see <https://www.gnu.org/licenses/>.
*/
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
    public class DiscChangerConverter : JsonConverter<DiscChanger>
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
                    if (discChanger == null)
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
                                Type discType = discChanger.GetDiscType();
                                Type discArrayType = discType.MakeArrayType();
                                Disc[] discList = (Disc[])JsonSerializer.Deserialize(ref reader, discArrayType, options);
                                foreach (var d in discList)
                                {
                                    discChanger.SetDisc(d.Slot, d);
                                }
                            }
                            break;
                        default:
                            if (reader.TokenType == JsonTokenType.String)
                                propertyValues.Add(propertyName, reader.GetString());
                            else if (reader.TokenType != JsonTokenType.Null)
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
                dl[i++] = kvp.Value;
            }
            Array.Sort(dl, (x, y) => ((Disc)x).CompareTo((Disc)y));
            JsonSerializer.Serialize(writer, dl, options);
            writer.WriteEndObject();
        }
    }
}
