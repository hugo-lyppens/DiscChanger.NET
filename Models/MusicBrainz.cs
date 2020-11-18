/*  Copyright 2020 Hugo Lyppens

    MusicBrainz.cs is part of DiscChanger.NET.

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
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using MimeTypes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MB = MetaBrainz.MusicBrainz;

namespace DiscChanger.Models
{
    public class MusicBrainz
    {
        public string musicBrainzPath;
        public string musicBrainzArtPath;
        public string musicBrainzRelPath;
        public string musicBrainzArtRelPath;

        public bool ShortenLastFrame { get; set; } = true;


        public class Track {
            public Track() { }
            public Track(Guid iD, TimeSpan? length, int? position, string title)
            {
                ID = iD;
                Length = length;
                Position = position;
                Title = title;
            }

            public Guid ID { get; set; }

            [JsonConverter(typeof(JsonTimeSpanConverter))]
            public System.TimeSpan? Length { get; set; }
            public int? Position { get; set; }
            public string Title { get; set; }
        };
        sealed class IntArrayComparer : EqualityComparer<int[]>
        {
            public override bool Equals(int[] x, int[] y)
              => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

            public override int GetHashCode(int[] x)
              => StructuralComparisons.StructuralEqualityComparer.GetHashCode(x);
        }

        public class Data
        {
            public int[] Lengths { get; set; }
            public int[] QueryTOC { get; set; }
            public string DiscID { get; set; }
            public string Title { get; set; }
            public string Artist { get; set; }
            public Track[] Tracks { get; set; }
            public Guid[] ReleaseIDs { get; set; }
            public Guid? ArtReleaseID { get; set; }
            public string ArtFileName { get; set; }
            public string ArtContentType { get; set; }
            public string[] URLs { get; set; }

            public string diagURL()
            {
                return "https://musicbrainz.org/ws/2/discid/" + (DiscID ?? "-") + "?toc=" + String.Join('+', QueryTOC);
            }
        }

        private ConcurrentDictionary<string, Data> discs = new ConcurrentDictionary<string, Data>();
        private ConcurrentDictionary<int[], string> lengths2Name = new ConcurrentDictionary<int[], string>(new IntArrayComparer());
        public MusicBrainz(string musicBrainzPath, string musicBrainzRelPath)
        {
            this.musicBrainzPath = musicBrainzPath;
            if (!Directory.Exists(musicBrainzPath))
                Directory.CreateDirectory(musicBrainzPath);
            this.musicBrainzArtPath = Path.Combine(musicBrainzPath, "Art");
            if (!Directory.Exists(musicBrainzArtPath))
                Directory.CreateDirectory(musicBrainzArtPath);
            this.musicBrainzRelPath = musicBrainzRelPath;
            this.musicBrainzArtRelPath = musicBrainzRelPath+"/Art";

            foreach( var name in Directory.GetFiles( musicBrainzPath, "*.json")) 
            {
                Data d = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path.Combine(musicBrainzPath, name)));

                string baseName = Path.GetFileNameWithoutExtension(name);
                discs[baseName] = d;
                lengths2Name[d.Lengths] = baseName;
            }
        }
        static private bool discMatch(IDisc disc, int[] lengths)
        {
            return disc.Offsets.Zip(disc.Offsets.Skip(1), (c, n) => n - c).Concat(new int[1] { disc.Sectors - disc.Offsets.LastOrDefault() }).SequenceEqual(lengths);
        }
        static private ulong discDiff(IDisc disc, int[] lengths)
        {
            if (disc==null||lengths==null||disc.Offsets.Count != lengths.Length)
                return UInt64.MaxValue;
            return disc.Offsets.Zip(disc.Offsets.Skip(1), (c, n) => n - c).Concat(new int[1] { disc.Sectors - disc.Offsets.LastOrDefault() }).Zip(lengths).Aggregate(0UL,(s,v)=> { ulong d = (ulong)(v.First - v.Second); s += d * d; return s; });
        }
        static readonly HashSet<char> blackList = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars().Concat(new char[] { '_','.','+' })); //plus added to deal with IIS double escaping rule

        internal Data Get(Disc d)
        {
            var lengths = d.StandardizedCDTableOfContents();
            if (lengths != null)
            { 
                if (lengths2Name.TryGetValue(lengths, out string name))
                {
                    return discs[name];
                }
            }
            return null;
        }

        internal Data Lookup(Disc d)
        {
            var inc = MB.Include.Artists | MB.Include.Labels | MB.Include.Recordings | MB.Include.ReleaseGroups | MB.Include.UrlRelationships;
            MB.Query query = null;
            MB.CoverArt.CoverArt coverArt = null;
            try
            {
                var lengths = d.StandardizedCDTableOfContents();
                if(lengths != null)
                {
                    if (lengths2Name.TryGetValue(lengths, out string name))
                    {
                        return discs[name];
                    }
                    int frameCount = lengths.Length;

                    int initial = 150;
                    var cumulative = lengths.Aggregate(new List<int>(frameCount + 4) { 1, frameCount, 0, initial }, (c, nxt) => { c.Add(c.Last() + nxt); return c; });
                    int total = cumulative.Last();
                    cumulative[2] = total;
                    var queryTOC = cumulative.Take(frameCount + 3);
                    var discTOC = MB.DiscId.TableOfContents.SimulateDisc(1, (byte)frameCount, queryTOC.Skip(2).ToArray());
                    var queryTOCArray = queryTOC.ToArray();
                    query = new MB.Query("DiscChangerApp");
                    coverArt = new MB.CoverArt.CoverArt("DiscChanger.NET", "0.1", "info@DiscChanger.NET");

                    var result = query.LookupDiscId(discTOC.DiscId, queryTOCArray, inc, true, true);
                    MB.Interfaces.Entities.IDisc disc = result.Disc;
                    IReadOnlyList<MB.Interfaces.Entities.IRelease> releases = disc != null ? disc.Releases : result.Releases;
                    Data data = new Data();
                    data.Lengths = lengths;
                    data.DiscID = disc?.Id;
                    data.QueryTOC = queryTOCArray;
                    int? trackCount = (d as DiscSony)?.DiscData?.TrackCount();
                    var rm = releases.Select(r =>
                    {
                        var m_discs = r.Media?.Where(m => m.Discs != null && m.Discs.Any());
                        var ml = m_discs?.Where(m => m.Discs.Any(md => disc != null ? md.Id == disc.Id : discMatch(md, lengths)));
                        var mt = ml?.Where(m => m.Tracks != null && m.Tracks.Any());
                        var m = mt?.FirstOrDefault(m => m.TrackCount == trackCount);
                        ulong min_diff = 0UL;
                        if (m == null)
                            m = mt?.FirstOrDefault();
                        if (m == null)
                        {
                            var m_diff = m_discs?.Select(m => Tuple.Create(m, Enumerable.Min(m.Discs.Select(md => discDiff(md, lengths))))).OrderByDescending(t => t.Item2);
                            var t = m_diff?.FirstOrDefault();
                            min_diff = t?.Item2 ?? Int64.MaxValue;
                            m = t?.Item1;
                        }
                        return Tuple.Create(r, min_diff, m?.Tracks);
                    }).OrderBy(t => t.Item2);

                    //                var selectedReleases = rm.Where(t => t.Item2 < Int64.MaxValue).Select(t => t.Item1);
                    var selectedReleases = rm.Where(t => t.Item2 < Int64.MaxValue).Where(t => t.Item2 == rm.FirstOrDefault()?.Item2).Select(t => t.Item1);
                    data.ReleaseIDs = selectedReleases.Select(r => r.Id).ToArray();
                    data.Tracks = rm.FirstOrDefault()?.Item3?.Select(t => new Track(t.Id, t.Length, t.Position, t.Title)).ToArray();
                    data.Artist = rm.FirstOrDefault(t => t.Item1.ArtistCredit.Count > 0)?.Item1.ArtistCredit.First().Name.Trim();
                    data.Title = rm.FirstOrDefault(t => !String.IsNullOrEmpty(t.Item1.Title))?.Item1.Title.Trim();
                    var URLs = selectedReleases.SelectMany(r => r.Relationships.Select(rel => rel.Url?.Resource?.AbsoluteUri).Where(s => !String.IsNullOrEmpty(s))).Distinct().ToArray();
                    data.URLs = URLs.Length > 0 ? URLs : null;
                    string fileNameArtist = new string((data.Artist ?? "ArtistUnk").Where(c => !Char.IsWhiteSpace(c) && !blackList.Contains(c)).Take(40).ToArray());
                    string fileNameTitle = new string((data.Title ?? "TitleUnk").Where(c => !Char.IsWhiteSpace(c) && !blackList.Contains(c)).Take(80).ToArray());
                    string fileNameBaseK = fileNameArtist + '_' + fileNameTitle;
                    string fileNameBase = fileNameBaseK;
                    int i = 1;
                    while (discs.ContainsKey(fileNameBase))
                    {
                        fileNameBase = fileNameBaseK + "_(" + i.ToString() + ')'; i++;
                    }

                    var releasesWithFront = selectedReleases.Where(rel => rel.CoverArtArchive.Front);
                    var artRelease = releasesWithFront.FirstOrDefault(r => r.Quality.ToLower() == "normal" && r.Packaging != null && r.Packaging.ToLower().Contains("jewel")) ?? releasesWithFront.FirstOrDefault();
                    var id = artRelease?.Id;
                    if (id != null)
                    {
                        //try
                        //{
                        var ca = coverArt.FetchFront(id.Value);
                        data.ArtReleaseID = id;
                        var ext = MimeTypeMap.GetExtension(ca.ContentType);
                        var fileNameArt = Path.ChangeExtension("CoverArtFront_" + fileNameBase, ext);
                        data.ArtContentType = ca.ContentType;
                        data.ArtFileName = fileNameArt;
                        using (var f = System.IO.File.OpenWrite(Path.Combine(this.musicBrainzArtPath, fileNameArt)))
                        {
                            ca.Data.Seek(0, System.IO.SeekOrigin.Begin);
                            ca.Data.CopyTo(f);
                        }
                        //}
                        //catch (WebException e)
                        //{
                        //    System.Diagnostics.Debug.WriteLine($"FetchFront {id.Value} Exception {e}");
                        //}
                    }

                    var fileName = Path.ChangeExtension(fileNameBase, "json");
                    using (var f = File.Create(Path.Combine(musicBrainzPath, fileName)))
                    {
                        var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                        JsonSerializer.Serialize(w, data);
                        f.Close();
                    }
                    discs[fileNameBase] = data;
                    lengths2Name[lengths] = fileNameBase;
                    return data;

                }
                return null;
            }
            finally
            {
                if(query!=null)
                    query.Dispose();
            }
        }
    }
}
