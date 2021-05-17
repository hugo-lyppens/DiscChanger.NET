/*  Copyright 2020 Hugo Lyppens

    MetaDataProvider.cs is part of DiscChanger.NET.

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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DiscChanger.Models
{
    public interface MetaDataProvider
    {
        public static int[] CDCommonFirstTrackLBAs = { 183,182,150 };
        public static readonly HashSet<char> blackList = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars().Concat(new char[] { '_', '.', '+' })); //plus added to deal with IIS double escaping rule
        public static string RemoveBlacklistedCharacters(string s, int maxLength)
        {
            return new string(s.Where(c => !Char.IsWhiteSpace(c) && !blackList.Contains(c)).Take(maxLength).ToArray());
        }

        public class Track
        {
            public Track() { }
            public Track(TimeSpan? length, int? position, string title)
            {
                Length = length;
                Position = position;
                Title = title;
            }

            [JsonConverter(typeof(JsonTimeSpanConverter))]
            public System.TimeSpan? Length { get; set; }
            public int? Position { get; set; }
            public string Title { get; set; }
        };

        public class IntArrayComparer : EqualityComparer<int[]>
        {
            public override bool Equals(int[] x, int[] y)
              => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

            public override int GetHashCode(int[] x)
              => StructuralComparisons.StructuralEqualityComparer.GetHashCode(x);
        }
        sealed class ByteArrayComparer : EqualityComparer<byte[]>
        {
            public override bool Equals(byte[] x, byte[] y)
              => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

            public override int GetHashCode(byte[] x)
              => StructuralComparisons.StructuralEqualityComparer.GetHashCode(x);
        }

    }
}
