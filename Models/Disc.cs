﻿/*  Copyright 2020 Hugo Lyppens

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
    public abstract class Disc : IEquatable<Disc>
    {
        public Disc(string slot) { Slot = slot; }
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

        public MusicBrainz.Data LookupData;
        public string Slot { get; set; }
        public DiscChangerModel DiscChanger;
        public System.DateTime DateTimeAdded;


        internal int CompareTo(Disc other)
        {
            if (Int32.TryParse(Slot, out int thisSlot) && Int32.TryParse(other.Slot, out int otherSlot))
                return thisSlot.CompareTo(otherSlot);
            return Slot.CompareTo(other.Slot);
        }

        public virtual void Clear() { Slot = null; LookupData = null; }
        public override bool Equals(object obj)
        {
            return Equals(obj as Disc);
        }
        public bool Equals(Disc other)
        {
            return other != null && this.Slot == other.Slot;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Slot);
        }
        public abstract int[] StandardizedCDTableOfContents();
        public static bool operator==(Disc lhs, Disc rhs)
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
        public virtual string getArtist()
        {
            return LookupData?.Artist;
        }
        public virtual string getTitle()
        {
            return LookupData?.Title;
        }
        public const string DiscTypeNone = "None";

        public virtual string getDiscType()
        {
            return DiscTypeNone;
        }
        public abstract IEnumerable<MusicBrainz.Track> getTracks();
        public virtual string toHtml(string artRelPath)
        {
            var afn = LookupData?.ArtFileName;
            var slotHtml = HtmlEncode(Slot ?? "--");
            StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-changer=""", 8192);
            sb.Append(DiscChanger.Key); sb.Append(@""" data-slot= """); sb.Append(slotHtml); sb.Append(@"""><div class=""disc-header""><span class=""slot"">");
            sb.Append(HtmlEncode(DiscChanger.Name)); sb.Append(':'); sb.Append(slotHtml);
            sb.Append(@"</span><span class=""disc-type"">");
            sb.Append(HtmlEncode(getDiscType() ?? "-"));
            sb.Append("</span></div>");

            if (afn != null && artRelPath != null)
            {
                sb.Append("<img src = \""); sb.Append(artRelPath); sb.Append('/'); sb.Append(HttpUtility.UrlEncode(afn)); sb.Append(@"""/>");
            }
            sb.Append(@"<div class=""artist"">");
            string a = getArtist();
            if (a != null)
                sb.Append(HtmlEncode(a));
            sb.Append(@"</div><div class=""title"">");
            string t = getTitle();
            if (t != null)
                sb.Append(HtmlEncode(t));
            sb.Append(@"</div>");
            sb.Append(@"<div class=""data"" style=""display:none"">");
            var tracks = getTracks();
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

        internal virtual bool isComplete()
        {
            return false;
        }
    }

    public abstract class DiscSony : Disc
    {
        public DiscSony(string slot): base(slot) { }

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
            return Equals(obj as DiscSony);
        }
        public bool Equals(DiscSony other)
        {
            return other != null && this.DiscData == other.DiscData && this.TableOfContents == other.TableOfContents;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(DiscData, TableOfContents);
        }
        public override string getDiscType()
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
        public static bool operator ==(DiscSony lhs, DiscSony rhs)
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

        public static bool operator !=(DiscSony lhs, DiscSony rhs)
        {
            return !(lhs == rhs);
        }
        public override IEnumerable<MusicBrainz.Track> getTracks()
        {
            IEnumerable<MusicBrainz.Track> tracks;
            tracks = LookupData?.Tracks;
            if (tracks == null && DiscData != null && DiscData.TrackCount() != null)
            {
                var tlist = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value);
                if (TableOfContents != null && TableOfContents.TitleFrames.TryGetValue("CD", out int[] framesList))
                    tracks = Enumerable.Zip(tlist, framesList, (track, frames) => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = track; t.Length = new TimeSpan(((long)frames * 10000000L) / 75L); return t; });
                else
                    tracks = tlist.Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = i; return t; });
            }
            else
            {
                if (tracks == null && DiscData != null && this.TableOfContents != null)
                    tracks = this.TableOfContents.Titles.Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = Int32.Parse(i); return t; });
            }
            return tracks;
        }

        internal override bool isComplete()
        {
            return this.DiscData?.DiscType == DiscTypeNone;
        }
    }


    public class DiscSonyDVD : DiscSony
    {
        public DiscSonyDVD(string slot):base( slot){}

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

        public Text DiscText { get; set; }
        public override void Clear() { DiscText = null; base.Clear(); }
//        public override bool HasAll() { return base.HasAll() && (DiscText != null/*||!(DiscData.HasMemo||DiscData.HasText)*/); }
        public override bool Equals(object obj)
        {
            return Equals(obj as DiscSonyDVD);
        }
        public bool Equals(DiscSonyDVD other)
        {
            return other != null && base.Equals( other ) && this.DiscText == other.DiscText;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), DiscText);
        }

        public override string getTitle()
        {
            return base.getTitle()??(DiscText?.TextString != null ? "CD-TEXT: " + DiscText.TextString : null);
        }

        internal override bool isComplete()
        {
            return (DiscData != null && DiscText != null && (TableOfContents != null || DiscData.DiscType.Contains("SACD"))) || base.isComplete();
        }

        //public override string toHtml(string artRelPath)
        //    {
        //        var afn = LookupData?.ArtFileName;
        //        var slotHtml = HtmlEncode(Slot ?? "--");
        //        StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-changer=""", 8192);
        //        sb.Append(DiscChanger.Key); sb.Append(@""" data-slot= """); sb.Append(slotHtml); sb.Append(@"""><div class=""disc-header""><span class=""slot"">");
        //        sb.Append(HtmlEncode(DiscChanger.Name)); sb.Append(':'); sb.Append(slotHtml);
        //        sb.Append(@"</span><span class=""disc-type"">");
        //        sb.Append(HtmlEncode(DiscData?.DiscType ?? "-"));
        //        sb.Append("</span></div>");

        //        if (afn != null && artRelPath != null)
        //        {
        //            sb.Append("<img src = \""); sb.Append(artRelPath); sb.Append('/'); sb.Append(HttpUtility.UrlEncode(afn)); sb.Append(@"""/>");
        //        }
        //        sb.Append(@"<div class=""artist"">");
        //        string a = LookupData?.Artist;
        //        if (a != null)
        //            sb.Append(HtmlEncode(a));
        //        sb.Append(@"</div><div class=""title"">");
        //        string t = LookupData?.Title ?? (DiscText?.TextString != null ? "CD-TEXT: " + DiscText.TextString : null);
        //        if (t != null)
        //            sb.Append(HtmlEncode(t));
        //        sb.Append(@"</div>");
        //        sb.Append(@"<div class=""data"" style=""display:none"">");
        //        var tracks = LookupData?.Tracks;
        //        if (tracks == null && DiscData != null && DiscData.TrackCount() != null)
        //            tracks = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value).Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = i; return t; }).ToArray();
        //        if (tracks != null)
        //        {
        //            sb.Append(@"<table class=""tracks"">");
        //            foreach (var track in tracks)
        //            {
        //                sb.Append(@"<tr onclick = ""dt('");
        //                sb.Append(DiscChanger.Key); sb.Append("',");
        //                sb.Append(slotHtml); sb.Append(',');
        //                sb.Append(track.Position);
        //                sb.Append(@")""><td>");
        //                sb.Append(track.Position);
        //                sb.Append(@"</td><td>"); sb.Append(HtmlEncode(track.Title ?? "---")); sb.Append("</td><td>");
        //                sb.Append(track.Length?.ToString(@"h\:mm\:ss") ?? "--");
        //                sb.Append(@"</td></tr>");
        //            }
        //            sb.Append(@"</table>");
        //        }
        //        if (LookupData != null)
        //        {
        //            sb.Append(@"<div class=""urls"">");
        //            var urls = LookupData?.URLs;
        //            if (urls != null)
        //                foreach (var url in urls)
        //                {
        //                    sb.Append(@"<a href = """); sb.Append(url); sb.Append(@""" target = ""_blank""></a>");
        //                }
        //            sb.Append(@"<a class=""diag"" href = """); sb.Append(LookupData.diagURL()); sb.Append(@""" target = ""_blank""></a>");
        //            sb.Append(@"</div>");
        //        }

        //        sb.Append(@"</div>");
        //        sb.Append(@"</div>");
        //        return sb.ToString();
        //    }
    }

    public class DiscSonyBD : DiscSony
    {
        public DiscSonyBD(string slot) : base(slot) { }

        enum discInfoDataType { AlbumTitleOrDiscName=0, TrackOrTitleName, AlbumOrDiscGenre, TrackOrTitleGenre };
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
            //            public int? DiscNumber { get; set; }
            //            public int? titleTrackNumber { get; set; }
            //            public byte type { get; set; }
            public string DiscTypeString { get; set; }
            public string AlbumTitleOrDiscName { get; set; }
            public string AlbumOrDiscGenre { get; set; }
            //            public string CharacterSet { get; set; }
            //            "0x00 : Album Title(CDDA) / Disc Name (DVD and BD)
            //0x01 : Track Name(CDDA) / Title Name(DVD and BD)
            //0x02 : Album Genre(CDDA) / Disc Genre(DVD and BD)
            //0x03 : Title Genre / Track Genre "							

            public enum DataType : byte { AlbumTitleOrDiscName, TrackOrTitleName, AlbumOrDiscGenre, TrackOrTitleGenre };
            public enum DiscType : byte { NoDisc=0x00, CDDA=0x01, DVD_ROM=0x03, BD_ROM=0x05, Unknown=0xff };
            static public readonly Dictionary<DiscSonyBD.Information.DiscType, string> discType2String = new Dictionary<DiscSonyBD.Information.DiscType, string> {
                { DiscSonyBD.Information.DiscType.NoDisc, "No Disc" },
                { DiscSonyBD.Information.DiscType.CDDA, "CDDA" },
                { DiscSonyBD.Information.DiscType.DVD_ROM, "DVD-ROM" },
                { DiscSonyBD.Information.DiscType.BD_ROM, "BD-ROM" },
                { DiscSonyBD.Information.DiscType.Unknown, "Unknown" }
            };

            public struct TrackOrTitle
            {
                public TrackOrTitle(int number, string name)
                {
                    Number = number;
                    Name = name;
                }

                public int Number { get; set; }
                public string Name { get; set; }
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
        }

        public Information DiscInformation { get; set; }
        public class IDData : IEquatable<IDData>
        {
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
        }
        public IDData DiscIDData { get; set; }

        public override void Clear() { DiscInformation = null; base.Clear(); }
//        public override bool HasAll() { return base.HasAll() && (DiscInformation != null/*||!(DiscData.HasMemo||DiscData.HasText)*/); }
        public override bool Equals(object obj)
        {
            return Equals(obj as DiscSonyBD);
        }
        public bool Equals(DiscSonyBD other)
        {
            return other != null && base.Equals(other) && this.DiscInformation == other.DiscInformation;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), DiscInformation);
        }
        public override string getTitle()
        {
            return LookupData?.Title ?? (DiscInformation?.AlbumTitleOrDiscName);
        }
        public override IEnumerable<MusicBrainz.Track> getTracks()
        {
            IEnumerable<MusicBrainz.Track> tracks;
            tracks = LookupData?.Tracks;
            if (tracks == null)
            {
                var tracksOrTitles = DiscInformation?.TracksOrTitles;
                if (tracksOrTitles != null)
                {
                    var tf = TableOfContents?.TitleFrames;
                    if (tf != null && tf.TryGetValue("CD", out int[] framesList) && framesList.Length >= tracksOrTitles.Count)
                        tracks = Enumerable.Zip(tracksOrTitles, framesList, (trkOrTitle, frames) => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = trkOrTitle.Number; t.Title = trkOrTitle.Name; t.Length = new TimeSpan(((long)frames * 10000L) / 75L); return t; }).ToArray();
                    else
                        tracks = tracksOrTitles.Select(trkOrTitle => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = trkOrTitle.Number; t.Title = trkOrTitle.Name; return t; });
                }
            }
            return tracks??base.getTracks();
        }

        internal override bool isComplete()
        {
            return (DiscData != null && 
                    (TableOfContents != null || (DiscData.DiscType!="DVD"&&DiscData.DiscType!="CD")) &&
                    (DiscIDData!=null&&DiscInformation!=null||(DiscData.DiscType!="DVD" && DiscData.DiscType != "CD" && DiscData.DiscType != "BD")))
                    || base.isComplete();
        }

        //public override string toHtml(string artRelPath)
        //{
        //    var afn = LookupData?.ArtFileName;
        //    var slotHtml = HtmlEncode(Slot ?? "--");
        //    StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-changer=""", 8192);
        //    sb.Append(DiscChanger.Key); sb.Append(@""" data-slot= """); sb.Append(slotHtml); sb.Append(@"""><div class=""disc-header""><span class=""slot"">");
        //    sb.Append(HtmlEncode(DiscChanger.Name)); sb.Append(':'); sb.Append(slotHtml);
        //    sb.Append(@"</span><span class=""disc-type"">");
        //    sb.Append(HtmlEncode(DiscData?.DiscType ?? "-"));
        //    sb.Append("</span></div>");

        //    if (afn != null && artRelPath != null)
        //    {
        //        sb.Append("<img src = \""); sb.Append(artRelPath); sb.Append('/'); sb.Append(HttpUtility.UrlEncode(afn)); sb.Append(@"""/>");
        //    }
        //    sb.Append(@"<div class=""artist"">");
        //    string a = LookupData?.Artist;
        //    if (a != null)
        //        sb.Append(HtmlEncode(a));
        //    sb.Append(@"</div><div class=""title"">");
        //    string t = LookupData?.Title ?? (DiscInformation?.AlbumTitleOrDiscName);
        //    if (t != null)
        //        sb.Append(HtmlEncode(t));
        //    sb.Append(@"</div>");
        //    sb.Append(@"<div class=""data"" style=""display:none"">");
        //    var tracks = LookupData?.Tracks;
        //    if (tracks == null && DiscData != null && DiscData.TrackCount() != null)
        //        tracks = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value).Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = i; return t; }).ToArray();
        //    if (tracks == null && DiscData != null && DiscData.TrackCount() != null)
        //        tracks = Enumerable.Range(this.DiscData.StartTrackTitleAlbum.Value, DiscData.TrackCount().Value).Select(i => { MusicBrainz.Track t = new MusicBrainz.Track(); t.Position = i; return t; }).ToArray();
        //    if (tracks != null)
        //    {
        //        sb.Append(@"<table class=""tracks"">");
        //        foreach (var track in tracks)
        //        {
        //            sb.Append(@"<tr onclick = ""dt('");
        //            sb.Append(DiscChanger.Key); sb.Append("',");
        //            sb.Append(slotHtml); sb.Append(',');
        //            sb.Append(track.Position);
        //            sb.Append(@")""><td>");
        //            sb.Append(track.Position);
        //            sb.Append(@"</td><td>"); sb.Append(HtmlEncode(track.Title ?? "---")); sb.Append("</td><td>");
        //            sb.Append(track.Length?.ToString(@"h\:mm\:ss") ?? "--");
        //            sb.Append(@"</td></tr>");
        //        }
        //        sb.Append(@"</table>");
        //    }
        //    if (LookupData != null)
        //    {
        //        sb.Append(@"<div class=""urls"">");
        //        var urls = LookupData?.URLs;
        //        if (urls != null)
        //            foreach (var url in urls)
        //            {
        //                sb.Append(@"<a href = """); sb.Append(url); sb.Append(@""" target = ""_blank""></a>");
        //            }
        //        sb.Append(@"<a class=""diag"" href = """); sb.Append(LookupData.diagURL()); sb.Append(@""" target = ""_blank""></a>");
        //        sb.Append(@"</div>");
        //    }

        //    sb.Append(@"</div>");
        //    sb.Append(@"</div>");
        //    return sb.ToString();
        //}
    }

}
