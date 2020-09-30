/*  Copyright 2020 Hugo Lyppens

    Disc.cs is part of DiscChanger.NET.

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
using System.Collections.Concurrent;
using System.Collections;
using System.Text;
using static System.Web.HttpUtility;
using System.Web;

namespace DiscChanger.Models
{
    //    public class Disc : IHostedService, IDisposable
    public class Disc : IEquatable<Disc>
    {
        public const string DiscTypeNone = "None";
        static public readonly Dictionary<byte, string> discType2String = new Dictionary<byte, string> {
            {(byte)0x00, DiscTypeNone },
            {(byte)0x01, "CD" },
            {(byte)0x02, "VCD/SVCD" },
            {(byte)0x03, "DVD" },
            {(byte)0x04, "DVDLCK" },//(playback impossible Region and parental lock and so on aren't released.)" },
            {(byte)0x05, "BD" },
            {(byte)0x10, "SACD-CD" },
            {(byte)0x11, "SACD-2CH" },
            {(byte)0x12, "SACD-MULTI_CH" },
            {(byte)0x20, "MP3" },
            {(byte)0x21, "DATA-CD-MUSIC" }, //( only Music file in Data Media )
            {(byte)0x22, "DATA-CD-PHOTO" }, // ( only Photo file in Data Media )
            {(byte)0x23, "DATA-CD-MUSICPHOTO" }, // ( Music and  Photo file in Data Media )
            {(byte)0x30, "DATA-DVD" }, // ( Unknown )
            {(byte)0x31, "DATA-DVD-MUSIC" }, // ( only Music file in Data Media )
            {(byte)0x32, "DATA-DVD-PHOTO" }, // ( only Photo file in Data Media )
            {(byte)0x33, "DATA-DVD-MUSICPHOTO" }, // ( Music and Photo file in Data Media)
            {(byte)0x40, "DATA-BD" }, // ( Unknown )
            {(byte)0x41, "DATA-BD-MUSIC" }, // ( only Music file in Data Media )
            {(byte)0x42, "DATA-BD-PHOTO" }, // ( only Photo file in Data Media )
            {(byte)0x43, "DATA-BD-MUSICPHOTO" }, // ( Music and Photo file in Data Media )
            {(byte)0xFF, "Unknown" }
        };


        static public readonly Dictionary<ushort, string> languageCode2String = new Dictionary<ushort, string> {
            { (ushort)0x0000, "DiscMemo" },
            { (ushort)0x1144, "English" }
        };

        static public readonly Dictionary<byte, string> characterCodeSet2String = new Dictionary<byte, string> {
            { (byte)0x00, "ISO-646(ASCII)" },
            { (byte)0x01, "ISO-8859" },
            { (byte)0x10, "Disc Memo(ASCII)" }
        };
        public MusicBrainz.Data LookupData;
        public string Slot { get; set; }
        public DiscChangerModel DiscChanger;
        public System.DateTime DateTimeAdded;

        public class Data : IEquatable<Data>
        {

//            public int? DiscNumber { get; set; }
            public string DiscType { get; set; }
            public int? StartTrackTitleAlbum { get; set; }
            public int? LastTrackTitleAlbum { get; set; }
            public int? TrackCount() { return LastTrackTitleAlbum+1- StartTrackTitleAlbum; }
            public bool HasText { get; set; }
            public bool HasMemo { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as Data);
            }

            public bool Equals(Data other)
            {
                return other != null &&
                       DiscType == other.DiscType &&
                       StartTrackTitleAlbum == other.StartTrackTitleAlbum &&
                       LastTrackTitleAlbum == other.LastTrackTitleAlbum &&
                       HasText == other.HasText &&
                       HasMemo == other.HasMemo;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(DiscType, StartTrackTitleAlbum, LastTrackTitleAlbum, HasText, HasMemo);
            }
            public static bool operator ==(Data lhs, Data rhs)
            {
                // Check for null on left side.
                if (Object.ReferenceEquals(lhs, null))
                {
                    if (Object.ReferenceEquals(rhs, null))
                    {
                        // null == null = true.
                        return true;
                    }

                    // Only the left side is null.
                    return false;
                }
                // Equals handles case of null on right side.
                return lhs.Equals(rhs);
            }

            public static bool operator !=(Data lhs, Data rhs)
            {
                return !(lhs == rhs);
            }
        };
        public class Text : IEquatable<Text>
        {
//            public int? DiscNumber { get; set; }
//            public int? titleTrackNumber { get; set; }
//            public byte type { get; set; }
            public string Language { get; set; }
//            public string CharacterSet { get; set; }
            public string TextString { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as Text);
            }

            public bool Equals(Text other)
            {
                return other != null &&
                       Language == other.Language &&
                       TextString == other.TextString;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Language, TextString);
            }
            public static bool operator ==(Text lhs, Text rhs)
            {
                // Check for null on left side.
                if (Object.ReferenceEquals(lhs, null))
                {
                    if (Object.ReferenceEquals(rhs, null))
                    {
                        // null == null = true.
                        return true;
                    }

                    // Only the left side is null.
                    return false;
                }
                // Equals handles case of null on right side.
                return lhs.Equals(rhs);
            }

            public static bool operator !=(Text lhs, Text rhs)
            {
                return !(lhs == rhs);
            }
        }

        public class TOC : IEquatable<TOC>
        {

            public string[] Titles { get; set; }
            public ConcurrentDictionary<string, int[]> TitleFrames { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as TOC);
            }

            public bool Equals(TOC other)
            {
                return other != null &&
                       StructuralComparisons.StructuralEqualityComparer.Equals(Titles, other.Titles) &&
                       Titles.All(t=>StructuralComparisons.StructuralEqualityComparer.Equals(TitleFrames[t], other.TitleFrames[t]));
            }
            //public override int GetHashCode()
            //{
            //    return HashCode.Combine;
            //}

            public static bool operator ==(TOC lhs, TOC rhs)
            {
                // Check for null on left side.
                if (Object.ReferenceEquals(lhs, null))
                {
                    if (Object.ReferenceEquals(rhs, null))
                    {
                        // null == null = true.
                        return true;
                    }

                    // Only the left side is null.
                    return false;
                }
                // Equals handles case of null on right side.
                return lhs.Equals(rhs);
            }

            public static bool operator !=(TOC lhs, TOC rhs)
            {
                return !(lhs == rhs);
            }
        }
        public TOC TableOfContents { get; set; }
        public Text DiscText { get; set; }
        public Data DiscData { get; set; }
        public void Clear() { TableOfContents = null; DiscText = null; DiscData = null; }
        public bool HasAll() { return DiscData != null && TableOfContents != null && (DiscText != null/*||!(DiscData.HasMemo||DiscData.HasText)*/); }
        public override bool Equals(object obj)
        {
            return Equals(obj as Disc);
        }
        public bool Equals(Disc other)
        {
            return other != null && this.DiscData == other.DiscData && this.DiscText == other.DiscText && this.TableOfContents == other.TableOfContents;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(DiscData, DiscText, TableOfContents);
        }
        public int[] StandardizedCDTableOfContents()
        {
            if (TableOfContents?.TitleFrames != null && TableOfContents.TitleFrames.TryGetValue("CD", out int[] lengths))
            {
                if (!this.DiscChanger.AdjustLastTrackLength || lengths.Length<1)
                    return lengths;
                var adjustedLengths = lengths.Clone() as int[];
                adjustedLengths[adjustedLengths.Length - 1] -= 1;
                return adjustedLengths;
            }
            return null;
        }
        public static bool operator ==(Disc lhs, Disc rhs)
        {
            // Check for null on left side.
            if (Object.ReferenceEquals(lhs, null))
            {
                if (Object.ReferenceEquals(rhs, null))
                {
                    // null == null = true.
                    return true;
                }

                // Only the left side is null.
                return false;
            }
            // Equals handles case of null on right side.
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Disc lhs, Disc rhs)
        {
            return !(lhs == rhs);
        }
        public string toHtml(string artRelPath)
        {
            var afn = LookupData?.ArtFileName;
            var slotHtml = HtmlEncode(Slot??"--");
            StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-changer=""", 8192);
            sb.Append(DiscChanger.Key); sb.Append(@""" data-slot= """);sb.Append(slotHtml);sb.Append(@"""><div class=""disc-header""><span class=""slot"">");
            sb.Append(HtmlEncode(DiscChanger.Name));sb.Append(':');sb.Append(slotHtml);
            sb.Append(@"</span><span class=""disc-type"">");
            sb.Append(HtmlEncode(DiscData?.DiscType ?? "-"));
            sb.Append("</span></div>");

            if (afn != null && artRelPath != null)
            {
                sb.Append("<img src = \""); sb.Append(artRelPath); sb.Append('/'); sb.Append(HttpUtility.UrlEncode(afn)); sb.Append(@"""/>");
            }
            sb.Append(@"<div class=""artist"">");
            string a = LookupData?.Artist;
            if (a != null)
                sb.Append(HtmlEncode(a));
            sb.Append(@"</div><div class=""title"">");
            string t = LookupData?.Title??(DiscText?.TextString!=null?"CD-TEXT: "+DiscText.TextString:null);
            if (t != null)
                sb.Append(HtmlEncode(t));
            sb.Append(@"</div>");
            sb.Append(@"<div class=""data"" style=""display:none"">");
            var tracks = LookupData?.Tracks;
            if (tracks == null && DiscData!=null&& DiscData.TrackCount()!=null)
                tracks = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value).Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = i; return t; }).ToArray();
            if (tracks != null)
            {
                sb.Append(@"<table class=""tracks"">");
                foreach (var track in tracks)
                {
                    sb.Append(@"<tr onclick = ""dt('");
                    sb.Append(DiscChanger.Key); sb.Append("',");
                    sb.Append(slotHtml); sb.Append(',');
                    sb.Append(track.Position);
                    sb.Append(@")""><td>");
                    sb.Append(track.Position);
                    sb.Append(@"</td><td>"); sb.Append(HtmlEncode(track.Title??"---")); sb.Append("</td><td>");
                    sb.Append(track.Length?.ToString(@"h\:mm\:ss") ?? "--");
                    sb.Append(@"</td></tr>");
                }
                sb.Append(@"</table>");
            }
            if (LookupData != null)
            {
                sb.Append(@"<div class=""urls"">");
                var urls = LookupData?.URLs;
                if (urls != null)
                    foreach (var url in urls)
                    {
                        sb.Append(@"<a href = """); sb.Append(url); sb.Append(@""" target = ""_blank""></a>");
                    }
                sb.Append(@"<a class=""diag"" href = """); sb.Append(LookupData.diagURL()); sb.Append(@""" target = ""_blank""></a>");
                sb.Append(@"</div>");
            }

            sb.Append(@"</div>");
            sb.Append(@"</div>");
            return sb.ToString();
        }
    }
}
