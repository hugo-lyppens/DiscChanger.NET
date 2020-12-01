/*  Copyright 2020 Hugo Lyppens

    JsonTimeSpanConverter.cs is part of DiscChanger.NET.

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
using System.Globalization;

namespace System.Text.Json.Serialization
{
	/// <summary>
	/// <see cref="JsonConverterFactory"/> to convert <see cref="TimeSpan"/> to and from strings. Supports <see cref="Nullable{TimeSpan}"/>.
	/// </summary>
	/// <remarks>
	/// TimeSpans are transposed using the constant ("c") format specifier: [-][d.]hh:mm:ss[.fffffff].
	/// </remarks>
	public class JsonTimeSpanConverter : JsonConverterFactory
	{
		/// <inheritdoc/>
		public override bool CanConvert(Type typeToConvert)
		{
			// Don't perform a typeToConvert == null check for performance. Trust our callers will be nice.
#pragma warning disable CA1062 // Validate arguments of public methods
			return typeToConvert == typeof(TimeSpan)
				|| (typeToConvert.IsGenericType && IsNullableTimeSpan(typeToConvert));
#pragma warning restore CA1062 // Validate arguments of public methods
		}

		/// <inheritdoc/>
		public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
		{
			// Don't perform a typeToConvert == null check for performance. Trust our callers will be nice.
#pragma warning disable CA1062 // Validate arguments of public methods
			return typeToConvert.IsGenericType
				? (JsonConverter)new JsonNullableTimeSpanConverter()
				: new JsonStandardTimeSpanConverter();
#pragma warning restore CA1062 // Validate arguments of public methods
		}

		private static bool IsNullableTimeSpan(Type typeToConvert)
		{
			Type UnderlyingType = Nullable.GetUnderlyingType(typeToConvert);

			return UnderlyingType != null && UnderlyingType == typeof(TimeSpan);
		}

		internal class JsonStandardTimeSpanConverter : JsonConverter<TimeSpan>
		{
			/// <inheritdoc/>
			public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				return reader.TokenType != JsonTokenType.String
					? throw new JsonException()
					: TimeSpan.ParseExact(reader.GetString(), "c", CultureInfo.InvariantCulture);
			}

			/// <inheritdoc/>
			public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
				=> writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
		}

		internal class JsonNullableTimeSpanConverter : JsonConverter<TimeSpan?>
		{
			/// <inheritdoc/>
			public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				//if(reader.TokenType==JsonTokenType.StartObject)
    //            {
				//	int ol = 1;
				//	TimeSpan? r=null;
				//	while (reader.Read())
				//	{
				//		if (reader.TokenType == JsonTokenType.EndObject)
				//		{
				//			ol--;
				//			if (ol == 0)
				//				return r;
				//		}
				//		else if (reader.TokenType == JsonTokenType.StartObject)
				//		{ ol++; }
				//		else if (reader.TokenType == JsonTokenType.PropertyName)
				//		{
				//			string propertyName = reader.GetString();
				//			if (propertyName == "Ticks")
				//			{
				//				reader.Read(); r = new TimeSpan(reader.GetInt64());
				//			}
				//		}
				//	}
    //            }
				return reader.TokenType != JsonTokenType.String
					? throw new JsonException()
					: TimeSpan.ParseExact(reader.GetString(), "c", CultureInfo.InvariantCulture);
			}

			/// <inheritdoc/>
			public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
				=> writer.WriteStringValue(value!.Value.ToString("c", CultureInfo.InvariantCulture));
		}
	}
}