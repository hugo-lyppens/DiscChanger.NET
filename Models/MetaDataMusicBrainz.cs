/*  Copyright 2020 Hugo Lyppens

    MetaDataMusicBrainz.cs is part of DiscChanger.NET.

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
using System.Threading.Tasks;
using System.Web;
using MB = MetaBrainz.MusicBrainz;

namespace DiscChanger.Models
{
    public class MetaDataMusicBrainz : MetaDataProvider
    {
        public const string Type = "MusicBrainz";
        public string musicBrainzPath;
        public string musicBrainzArtPath;
        public string musicBrainzRelPath;
        public string musicBrainzArtRelPath;

        public bool ShortenLastFrame { get; set; } = true;

        public class Track : MetaDataProvider.Track
        {
            public Track() { }
            public Track(Guid iD, TimeSpan? length, int? position, string title):base(length, position, title)
            {
                ID = iD;
            }

            public Guid ID { get; set; }
        };
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
            public string GetArtFileURL()
            {
                return ArtFileName != null ? ArtRelPath + '/' + HttpUtility.UrlEncode(ArtFileName) : null;
            }
            public string ArtContentType { get; set; }
            public string[] URLs { get; set; }

            public string diagURL()
            {
                return "https://musicbrainz.org/ws/2/discid/" + (DiscID ?? "-") + "?toc=" + String.Join('+', QueryTOC);
            }
            public string ArtRelPath;
        }

        private ConcurrentDictionary<string, Data> discs = new ConcurrentDictionary<string, Data>();
        private ConcurrentDictionary<int[], string> lengths2Name = new ConcurrentDictionary<int[], string>(new MetaDataProvider.IntArrayComparer());
        public MetaDataMusicBrainz(string musicBrainzPath, string musicBrainzRelPath)
        {
            this.musicBrainzPath = musicBrainzPath;
            this.musicBrainzArtPath = Path.Combine(musicBrainzPath, "Art");
            var dirMusicBrainz = Directory.CreateDirectory(musicBrainzPath);
            _ = Directory.CreateDirectory(musicBrainzArtPath);
            this.musicBrainzRelPath = musicBrainzRelPath;
            this.musicBrainzArtRelPath = musicBrainzRelPath + "/Art";

            foreach (var fileInfo in dirMusicBrainz.GetFiles("*.json"))
            {
                try
                {
                    Data d = JsonSerializer.Deserialize<Data>(File.ReadAllText(fileInfo.FullName));
                    d.ArtRelPath = musicBrainzArtRelPath;
                    string baseName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    discs[baseName] = d;
                    lengths2Name[d.Lengths] = baseName;
                }
                catch(Exception e) 
                {
                    System.Diagnostics.Debug.WriteLine($"Error {e.Message} reading MusicBrainz metadata {fileInfo.FullName}");
                }
            }
        }
        static private bool discMatch(IDisc disc, int[] lengths)
        {
            return disc.Offsets.Zip(disc.Offsets.Skip(1), (c, n) => n - c).Concat(new int[1] { disc.Sectors - disc.Offsets.LastOrDefault() }).SequenceEqual(lengths);
        }
        static private ulong discDiff(IDisc disc, int[] lengths)
        {
            if (disc == null || lengths == null || disc.Offsets.Count != lengths.Length)
                return UInt64.MaxValue;
            return disc.Offsets.Zip(disc.Offsets.Skip(1), (c, n) => n - c).Concat(new int[1] { disc.Sectors - disc.Offsets.LastOrDefault() }).Zip(lengths).Aggregate(0UL, (s, v) => { ulong d = (ulong)(v.First - v.Second); s += d * d; return s; });
        }

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
        
        internal async Task<Data> RetrieveMetaData(Disc d)
        {
            var inc = MB.Include.Artists | MB.Include.Labels | MB.Include.Recordings | MB.Include.ReleaseGroups | MB.Include.UrlRelationships;
            MB.Query query = null;
            MB.CoverArt.CoverArt coverArt = null;
            try
            {
                var lengths = d.StandardizedCDTableOfContents();
                if (lengths != null)
                {
                    if (lengths2Name.TryGetValue(lengths, out string name))
                    {
                        return discs[name];
                    }
                    int frameCount = lengths.Length;
                    MB.Interfaces.Entities.IDisc disc = null;
                    MB.Interfaces.IDiscIdLookupResult result = null;
                    int[] queryTOCArray=null;
                    int[] firstTrackLBAs = MetaDataProvider.CDCommonFirstTrackLBAs;
                    string graceNoteDiscID = (d as DiscSonyBD)?.DiscIDData?.GraceNoteDiscID;
                    if (graceNoteDiscID != null)
                    {
                        int spaceIndex = graceNoteDiscID.IndexOf(' ');
                        if(spaceIndex>0 && Int32.TryParse(graceNoteDiscID.Substring(0,spaceIndex), out int firstTrackLBA))
                            firstTrackLBAs = new int[] { firstTrackLBA };
                    }
                    foreach (int initial in firstTrackLBAs)
                    {
                        //                    int initial = 150;
                        var cumulative = lengths.Aggregate(new List<int>(frameCount + 4) { 1, frameCount, 0, initial }, (c, nxt) => { c.Add(c.Last() + nxt); return c; });
                        int total = cumulative.Last();
                        cumulative[2] = total;
                        var queryTOC = cumulative.Take(frameCount + 3);
                        var discTOC = MB.DiscId.TableOfContents.SimulateDisc(1, (byte)frameCount, queryTOC.Skip(2).ToArray());
                        queryTOCArray = queryTOC.ToArray();
                        query = new MB.Query("DiscChanger.NET", "1.5.0");
                        result = await query.LookupDiscIdAsync(discTOC.DiscId, queryTOCArray, inc, true, true);
                        disc = result.Disc;
                        if (disc != null)
                            break;
                    }
                    coverArt = new MB.CoverArt.CoverArt("DiscChanger.NET", "0.1", "info@DiscChanger.NET");
                    IReadOnlyList<MB.Interfaces.Entities.IRelease> releases = disc != null ? disc.Releases : result.Releases;
                    if (releases == null||releases.Count==0)
                        return null;
                    Data data = new Data();
                    data.ArtRelPath = musicBrainzArtRelPath;
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
                    string fileNameArtist = MetaDataProvider.RemoveBlacklistedCharacters(data.Artist ?? "ArtistUnk", 40);
                    string fileNameTitle = MetaDataProvider.RemoveBlacklistedCharacters(data.Title ?? "TitleUnk", 80);
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
                if (query != null)
                    query.Dispose();
            }
        }
    }
}
