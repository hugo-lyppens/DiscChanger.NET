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
using System.Xml.Serialization;

namespace DiscChanger.Models
{
    //    public class Disc : IHostedService, IDisposable
    public abstract class Disc
    {
        protected Disc() { }
        protected Disc(string slot) { Slot = slot; }
        public static Comparer<string> SlotComparer = Comparer<string>.Create((s1, s2) =>
        {
            if (Int32.TryParse(s1, out int v1) && Int32.TryParse(s2, out int v2))
                return v1.CompareTo(v2);
            else
                return s1.CompareTo(s2);
        });
        public static Comparer<KeyValuePair<string, Disc>> SlotDiscPairComparer = Comparer<KeyValuePair<string, Disc>>.Create((p1, p2) =>
        {
            return SlotComparer.Compare(p1.Key, p2.Key);
            //if (Int32.TryParse(p1.Key, out int v1) && Int32.TryParse(p2.Key, out int v2))
            //    return v1.CompareTo(v2);
            //else
            //    return p1.Key.CompareTo(p2.Key);
        });

        public MetaDataMusicBrainz.Data DataMusicBrainz;
        public MetaDataGD3.Match DataGD3Match;
        public string Slot { get; set; }
        public DiscChanger DiscChanger;
        public System.DateTime? DateTimeAdded { get; set; }
        public abstract bool IsCD();
        public abstract bool IsDVD();
        public abstract bool IsBD();
        public virtual bool NeedsMetaData(string type)
        {
            bool b = IsCD();
            if (!b && !IsDVD() && !IsBD())
                return false;
            switch (type)
            {
                case MetaDataMusicBrainz.Type:
                    return DataMusicBrainz == null && b;
                case MetaDataGD3.Type_Match:
                    return DataGD3Match == null;
                case MetaDataGD3.Type_MetaData:
                    return DataGD3Match != null && !DataGD3Match.HasMetaData();
                default:
                    throw new Exception("NeedsMetaData unknown type: " + type);
            }
        }

        internal int CompareTo(Disc other)
        {
            if (Int32.TryParse(Slot, out int thisSlot) && Int32.TryParse(other.Slot, out int otherSlot))
                return thisSlot.CompareTo(otherSlot);
            return Slot.CompareTo(other.Slot);
        }

        public virtual void Clear() { Slot = null; DataMusicBrainz = null; }
        public override bool Equals(object obj)
        {
            return obj is Disc other && this.Slot == other.Slot;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Slot);
        }
        public abstract int[] StandardizedCDTableOfContents();
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
        public virtual string GetArtist()
        {
            return DataGD3Match?.GetArtist() ?? DataMusicBrainz?.Artist;
        }
        public virtual string getTitle()
        {
            return DataGD3Match?.GetTitle() ?? DataMusicBrainz?.Title;
        }

        public const string DiscTypeNone = "None";

        public virtual string GetDiscType()
        {
            return DiscTypeNone;
        }
        public abstract IEnumerable<MetaDataProvider.Track> GetTracks();
        public virtual string ToHtml()
        {
            var afp = DataGD3Match?.GetArtFileURL() ?? DataMusicBrainz?.GetArtFileURL();
            var slotHtml = HtmlEncode(Slot ?? "--");
            StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-changer=""", 8192);
            sb.Append(DiscChanger.Key); sb.Append(@""" data-slot= """); sb.Append(slotHtml); sb.Append(@"""><div class=""disc-header""><span class=""slot"">");
            sb.Append(HtmlEncode(DiscChanger.Name)); sb.Append(':'); sb.Append(slotHtml);
            sb.Append(@"</span><span class=""disc-type"">");
            sb.Append(HtmlEncode(GetDiscType() ?? "-"));
            sb.Append("</span></div>");

            if (afp != null)
            {
                sb.Append("<img src = \""); sb.Append(afp); sb.Append(@"""/>");
            }
            sb.Append(@"<div class=""artist"">");
            string a = GetArtist();
            if (a != null)
                sb.Append(HtmlEncode(a));
            sb.Append(@"</div><div class=""title"">");
            string t = getTitle();
            if (t != null)
                sb.Append(HtmlEncode(t));
            sb.Append(@"</div>");
            sb.Append(@"<div class=""data"" style=""display:none"">");
            var tracks = GetTracks();
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
                    sb.Append(@"</td><td>"); sb.Append(HtmlEncode(track.Title ?? "---")); sb.Append("</td><td>");
                    sb.Append(track.Length?.ToString(@"h\:mm\:ss") ?? "--");
                    sb.Append(@"</td></tr>");
                }
                sb.Append(@"</table>");
            }
            if (DataMusicBrainz != null)
            {
                sb.Append(@"<div class=""urls"">");
                var urls = DataMusicBrainz?.URLs;
                if (urls != null)
                    foreach (var url in urls)
                    {
                        sb.Append(@"<a href = """); sb.Append(url); sb.Append(@""" target = ""_blank""></a>");
                    }
                sb.Append(@"<a class=""diag"" href = """); sb.Append(DataMusicBrainz.diagURL()); sb.Append(@""" target = ""_blank""></a>");
                sb.Append(@"</div>");
            }

            sb.Append(@"</div>");
            sb.Append(@"</div>");
            return sb.ToString();
        }

        public virtual bool HasBroadcastData()
        {
            return false;
        }
    }

    public abstract class DiscSony : Disc
    {
        protected DiscSony() { }
        protected DiscSony(string slot) : base(slot) { }
        public override bool IsCD()
        {
            return TableOfContents?.TitleFrames?.ContainsKey("CD") ?? false;//could be CD or hybrid SACD with known toc from CD layer
        }
        public override bool IsDVD()
        {
            return DiscData?.DiscType?.StartsWith("DVD") ?? false;
        }
        public override bool IsBD()
        {
            return DiscData?.DiscType == "BD";
        }

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


        //static public readonly Dictionary<ushort, string> languageCode2String = new Dictionary<ushort, string> {
        //    { (ushort)0x0000, "DiscMemo" },
        //    { (ushort)0x1144, "English" }
        //};

        //static public readonly Dictionary<byte, string> characterCodeSet2String = new Dictionary<byte, string> {
        //    { (byte)0x00, "ISO-646(ASCII)" },
        //    { (byte)0x01, "ISO-8859" },
        //    { (byte)0x10, "Disc Memo(ASCII)" }
        //};

        public class Data : IEquatable<Data>
        {

            //            public int? DiscNumber { get; set; }
            public string DiscType { get; set; }
            public int? StartTrackTitleAlbum { get; set; }
            public int? LastTrackTitleAlbum { get; set; }
            public int? TrackCount() { return LastTrackTitleAlbum + 1 - StartTrackTitleAlbum; }
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


        public class TOC : IEquatable<TOC>
        {
            [XmlAttribute]
            public string Source { get; set; }
            public string[] Titles { get; set; }
            public ConcurrentDictionary<string, int[]> TitleFrames { get; set; }
            public int PacketCount = 0;
            public TOC ShallowCopy()
            {
                return (TOC)MemberwiseClone();
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TOC);
            }

            public bool Equals(TOC other)
            {
                return other != null &&
                       StructuralComparisons.StructuralEqualityComparer.Equals(Titles, other.Titles) &&
                       Titles.All(t => StructuralComparisons.StructuralEqualityComparer.Equals(TitleFrames[t], other.TitleFrames[t]));
            }
            public override int GetHashCode()
            {
                HashCode hash = new HashCode();
                hash.Add(StructuralComparisons.StructuralEqualityComparer.GetHashCode(Titles));
                foreach (var t in Titles)
                    hash.Add(StructuralComparisons.StructuralEqualityComparer.GetHashCode(TitleFrames[t]));
                return hash.ToHashCode();
            }

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
        public Data DiscData { get; set; }
        public override void Clear() { TableOfContents = null; DiscData = null; base.Clear(); }
        //        public virtual bool HasAll() { return DiscData != null && TableOfContents != null; }
        public override bool Equals(object obj)
        {
            return obj is DiscSony other && base.Equals(other) && this.DiscData == other.DiscData && this.TableOfContents == other.TableOfContents;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(DiscData, TableOfContents);
        }
        public override string GetDiscType()
        {
            return DiscData?.DiscType;
        }
        public override int[] StandardizedCDTableOfContents()
        {
            if (TableOfContents?.TitleFrames != null && TableOfContents.TitleFrames.TryGetValue("CD", out int[] lengths))
            {
                if (!((DiscChangerSony)DiscChanger).AdjustLastTrackLength || lengths.Length < 1)
                    return lengths;
                var adjustedLengths = lengths.Clone() as int[];
                adjustedLengths[adjustedLengths.Length - 1] -= 1;
                return adjustedLengths;
            }
            return null;
        }

        public override IEnumerable<MetaDataProvider.Track> GetTracks()
        {
            IEnumerable<MetaDataProvider.Track> tracks;
            tracks = DataGD3Match?.GetTracks() ?? DataMusicBrainz?.Tracks;
            if (tracks != null || DiscData==null)
                return tracks;
            if ( DiscData.TrackCount() != null)
            {
                var tlist = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value);
                if (TableOfContents != null && TableOfContents.TitleFrames.TryGetValue("CD", out int[] framesList))
                    tracks = Enumerable.Zip(tlist, framesList, (track, frames) => new MetaDataProvider.Track() { Position = track, Length = new TimeSpan(((long)frames * 10000000L) / 75L) });
                else
                    tracks = tlist.Select(i => new MetaDataProvider.Track() { Position = i });
            }
            else if (TableOfContents != null)
                tracks = TableOfContents.Titles.Select(i => new MetaDataProvider.Track(){ Position = Int32.Parse(i) });
            return tracks;
        }

        public override bool HasBroadcastData()
        {
            return this.DiscData?.DiscType == DiscTypeNone;
        }
    }


    public class DiscSonyDVD : DiscSony
    {
        public DiscSonyDVD() { }

        public DiscSonyDVD(string slot) : base(slot) { }

        static public readonly Dictionary<ushort, string> languageCode2String = new Dictionary<ushort, string> {
            { (ushort)0x0000, "DiscMemo" },
            { (ushort)0x1144, "English" }
        };

        static public readonly Dictionary<byte, string> characterCodeSet2String = new Dictionary<byte, string> {
            { (byte)0x00, "ISO-646(ASCII)" },
            { (byte)0x01, "ISO-8859" },
            { (byte)0x10, "Disc Memo(ASCII)" }
        };

        public class Text : IEquatable<Text>
        {
            public string Language { get; set; }
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

        public Text DiscText { get; set; }
        public override void Clear() { DiscText = null; base.Clear(); }
        //        public override bool HasAll() { return base.HasAll() && (DiscText != null/*||!(DiscData.HasMemo||DiscData.HasText)*/); }
        public override bool Equals(object obj)
        {
            return obj is DiscSonyDVD other && base.Equals(other) && this.DiscText == other.DiscText;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), DiscText);
        }

        public override string getTitle()
        {
            string t = DataGD3Match?.GetTitle();
            if (t == null && DataMusicBrainz?.DiscID != null) //exact MusicBrainz match
                t = DataMusicBrainz?.Title;
            if (t == null&& DiscText?.TextString!=null)
                t = "CD-TEXT: " + DiscText.TextString;
            if (t == null)
                t = DataMusicBrainz?.Title; //inexact MusicBrainz match
            return t;
        }
        public override bool HasBroadcastData()
        {
            return (DiscData != null &&
                    (TableOfContents != null || (DiscData.DiscType != "DVD" && DiscData.DiscType != "CD")) &&
                    (DiscText != null || (DiscData.DiscType != "DVD" && DiscData.DiscType != "CD" && !DiscData.DiscType.StartsWith("SACD"))))
                    || base.HasBroadcastData();
        }

    }

    public class DiscSonyBD : DiscSony
    {
        public DiscSonyBD() { }

        public DiscSonyBD(string slot) : base(slot) { }

        enum discInfoDataType { AlbumTitleOrDiscName = 0, TrackOrTitleName, AlbumOrDiscGenre, TrackOrTitleGenre };
        //"0x00 : Album Title(CDDA) / Disc Name (DVD and BD)
        //0x01 : Track Name(CDDA) / Title Name(DVD and BD)
        //0x02 : Album Genre(CDDA) / Disc Genre(DVD and BD)
        //0x03 : Title Genre / Track Genre "							


        static public readonly Dictionary<ushort, string> languageCode2String = new Dictionary<ushort, string> {
            { (ushort)0x0000, "DiscMemo" },
            { (ushort)0x1144, "English" }
        };

        static public readonly Dictionary<byte, string> characterCodeSet2String = new Dictionary<byte, string> {
            { (byte)0x00, "ISO-646(ASCII)" },
            { (byte)0x01, "ISO-8859" },
            { (byte)0x10, "Disc Memo(ASCII)" }
        };

        public class Information : IEquatable<Information>
        {
            public string DiscTypeString { get; set; }
            public string AlbumTitleOrDiscName { get; set; }
            public string AlbumOrDiscGenre { get; set; }

            public enum DataType : byte { AlbumTitleOrDiscName, TrackOrTitleName, AlbumOrDiscGenre, TrackOrTitleGenre };
            public enum DiscType : byte { NoDisc = 0x00, CDDA = 0x01, DVD_ROM = 0x03, BD_ROM = 0x05, Unknown = 0xff };
            static public readonly Dictionary<DiscSonyBD.Information.DiscType, string> discType2String = new Dictionary<DiscSonyBD.Information.DiscType, string> {
                { DiscSonyBD.Information.DiscType.NoDisc, "No Disc" },
                { DiscSonyBD.Information.DiscType.CDDA, "CDDA" },
                { DiscSonyBD.Information.DiscType.DVD_ROM, "DVD-ROM" },
                { DiscSonyBD.Information.DiscType.BD_ROM, "BD-ROM" },
                { DiscSonyBD.Information.DiscType.Unknown, "Unknown" }
            };

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
            public struct TrackOrTitle
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
            {
                public TrackOrTitle(int number, string name)
                {
                    Number = number;
                    Name = name;
                }

                public int Number { get; set; }
                public string Name { get; set; }
                public static bool operator ==(TrackOrTitle t1, TrackOrTitle t2)
                {
                    return t1.Equals(t2);
                }
                public static bool operator !=(TrackOrTitle t1, TrackOrTitle t2)
                {
                    return !t1.Equals(t2);
                }
            }
            public List<TrackOrTitle> TracksOrTitles { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as Information);
            }

            public bool Equals(Information other)
            {
                return other != null &&
                       DiscTypeString == other.DiscTypeString &&
                       AlbumTitleOrDiscName == other.AlbumTitleOrDiscName &&
                       AlbumOrDiscGenre == other.AlbumOrDiscGenre &&
                       StructuralComparisons.StructuralEqualityComparer.Equals(TracksOrTitles, other.TracksOrTitles);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(base.GetHashCode(), AlbumOrDiscGenre, AlbumTitleOrDiscName, DiscTypeString, StructuralComparisons.StructuralEqualityComparer.GetHashCode(TracksOrTitles));
            }
            public static bool operator ==(Information lhs, Information rhs)
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

            public static bool operator !=(Information lhs, Information rhs)
            {
                return !(lhs == rhs);
            }

        }

        public Information DiscInformation { get; set; }
        public class IDData : IEquatable<IDData>
        {
            public enum DataTypeSerial : byte { GraceNoteDiscID = 1, AACSDiscID = 2 };
            //"0x00 : Information of IP_DISC_ID_DATA (Current Disc ID for Gracenote)
            //0x01 : Information of IP_DISC_ID_DATA(Current Disc ID from AACS file)
            //0x02 : Current Disc ID for Gracenote
            //0x03 : Current Disc ID from AACS file(valid if BD is mounted )
            //0x04 – 0xff  : reserved  "							
            public enum DataTypeNetwork : byte { GraceNoteDiscID_Info = 0, AACSDiscID_Info = 1, GraceNoteDiscID = 2, AACSDiscID = 3 };

            public int AACSDiscIDPacketCount = 0;
            public int GraceNoteDiscIDPacketCount = 0;

            public string GraceNoteDiscID { get; set; }
            public byte[] AACSDiscID { get; set; }
            public override bool Equals(object obj)
            {
                return Equals(obj as IDData);
            }

            public bool Equals(IDData other)
            {
                return other != null &&
                       GraceNoteDiscID == other.GraceNoteDiscID &&
                       StructuralComparisons.StructuralEqualityComparer.Equals(AACSDiscID, other.AACSDiscID);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(base.GetHashCode(), GraceNoteDiscID, StructuralComparisons.StructuralEqualityComparer.GetHashCode(AACSDiscID));
            }
            public static bool operator ==(IDData lhs, IDData rhs)
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

            public static bool operator !=(IDData lhs, IDData rhs)
            {
                return !(lhs == rhs);
            }
        }
        public IDData DiscIDData { get; set; }

        public override void Clear() { DiscInformation = null; DiscIDData = null; base.Clear(); }
        //        public override bool HasAll() { return base.HasAll() && (DiscInformation != null/*||!(DiscData.HasMemo||DiscData.HasText)*/); }
        public override bool Equals(object obj)
        {
            return obj is DiscSonyBD other && base.Equals(other) && this.DiscInformation == other.DiscInformation && this.DiscIDData == other.DiscIDData;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), DiscInformation, DiscIDData);
        }
        public override string getTitle()
        {
            string t = DataGD3Match?.GetTitle();
            if (t == null && DataMusicBrainz?.DiscID != null) //exact MusicBrainz match
                t = DataMusicBrainz?.Title;
            if (t == null)
                t = DiscInformation?.AlbumTitleOrDiscName;
            if (t == null)
                t=DataMusicBrainz?.Title; //inexact MusicBrainz match
            return t;
        }
        public override IEnumerable<MetaDataProvider.Track> GetTracks()
        {
            IEnumerable<MetaDataProvider.Track> tracks = DataGD3Match?.GetTracks();
            if (tracks != null)
                return tracks;
            if (DataMusicBrainz?.DiscID == null || DataMusicBrainz?.Tracks == null) //DiscID null means imprecise MusicBrainz match
            {
                var tracksOrTitles = DiscInformation?.TracksOrTitles;
                if (tracksOrTitles != null)
                {
                    var tf = TableOfContents?.TitleFrames;
                    if (tf != null && tf.TryGetValue("CD", out int[] framesList) && framesList.Length >= tracksOrTitles.Count)
                        tracks = Enumerable.Zip(tracksOrTitles, framesList, (trkOrTitle, frames) => new MetaDataProvider.Track { Position = trkOrTitle.Number, Title = trkOrTitle.Name, Length = new TimeSpan(((long)frames * 10000000L) / 75L) }).ToArray();
                    else
                        tracks = tracksOrTitles.Select(trkOrTitle => new MetaDataProvider.Track { Position = trkOrTitle.Number, Title = trkOrTitle.Name });
                    return tracks;
                }
            }
            tracks = base.GetTracks();
            return tracks;
        }

        public override bool HasBroadcastData()
        {
            return (DiscData != null &&
                    (TableOfContents != null || (DiscData.DiscType != "DVD" && DiscData.DiscType != "CD")) &&
                    (DiscInformation != null || (DiscData.DiscType != "DVD" && DiscData.DiscType != "CD" && DiscData.DiscType != "BD")))
                    || base.HasBroadcastData();
        }
    }
}
