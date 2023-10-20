/*  Copyright 2020 Hugo Lyppens

    MetaDataGD3.cs is part of DiscChanger.NET.

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
using Microsoft.AspNetCore.DataProtection;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace DiscChanger.Models
{
    public class MetaDataGD3 : MetaDataProvider
    {
        public const string Type_Match = "GD3 Match";
        public const string Type_MetaData = "GD3 MetaData";

        public string GD3Path;
        public string GD3CDPath;
        public string GD3DVDPath;
        public string GD3BDPath;
        public string GD3RelPath;
        public string GD3CDRelPath;
        public string GD3DVDRelPath;
        public string GD3BDRelPath;

        public bool ShortenLastFrame { get; set; } = true;


        public abstract class MetaData
        {
            public string ImageFileName { get; set; }
        }
        public class MetaDataCD : MetaData
        {
            public int AlbumCode { get; set; }
            public GD3.AlbumMeta AlbumMeta { get; set; }
            public IEnumerable<MetaDataProvider.Track> GetTracks()
            {
                var tm = AlbumMeta?.trackMeta;
                if (tm == null || tm.Length==0)
                    return null;
                //Data quality issue: sometimes the GD3 track lengths are in seconds, sometimes in milliseconds. If longest track is less than 10,000, assume unit is seconds
                if(Enumerable.Max(tm.Select(t=>t.TrackLength))<10000)
                    return tm.Select(t => new MetaDataProvider.Track { Title = t.TrackName, Position = t.TrackNumber, Length = TimeSpan.FromSeconds(t.TrackLength) });
                else
                    return tm.Select(t => new MetaDataProvider.Track { Title = t.TrackName, Position = t.TrackNumber, Length = TimeSpan.FromMilliseconds(t.TrackLength) });
            }
        }
        public class MetaDataDVD : MetaData
        {
            public int DVDCode { get; set; }
            public int DiscID { get; set; }
            public GD3DVD.DVDMeta DVDMeta { get; set; }
        }

        public abstract class Match
        {
            public int? SelectedMatch { get; set; }
            public virtual IEnumerable<MetaDataProvider.Track> GetTracks() { return SelectedMatch!=null?tracks:null; }
            protected IEnumerable<MetaDataProvider.Track> tracks=null;
            public abstract string GetArtFileURL();
            public abstract string GetTitle();
            public abstract string GetArtist();
            public virtual string GetPlot() { return null; }


            public abstract bool HasMetaData();
            public abstract Task<bool> RetrieveMetaData();
            public abstract bool AssociateMetaData();
            protected MetaDataGD3 metaDataGD3;
            public void SetMetaDataGD3(MetaDataGD3 metaDataGD3) => this.metaDataGD3 = metaDataGD3;
        }

        public static async Task<string> WriteImage(byte[] image, string path, string fileNameBase)
        {
            var imageInfo = Image.Identify(image);
            var format = imageInfo.Metadata.DecodedImageFormat;
            var ext = format?.FileExtensions?.FirstOrDefault();
            var albumImageFileName = Path.ChangeExtension(fileNameBase, ext);
            using (var f = File.Create(Path.Combine(path, albumImageFileName)))
            {
                await f.WriteAsync(image);
            }
            return albumImageFileName;
        }
        public class MatchCD : Match
        {
            public MatchCD(MetaDataGD3 m) { metaDataGD3 = m; }
            public MatchCD() {}

            public int[] Lengths { get; set; }
            public string GraceNoteDiscID { get; set; }
            public GD3.AlbumCode[] Matches { get; set; }
            private MetaDataCD metaData;
            public override bool HasMetaData() => metaData != null;
            public override string GetArtFileURL()
            {
                if (SelectedMatch == null)
                    return null;
                var fn = metaData?.ImageFileName;
                return fn != null ? metaDataGD3.GD3CDRelPath + '/' + HttpUtility.UrlEncode(fn) : null;
            }
            public override IEnumerable<MetaDataProvider.Track> GetTracks()
            {
                if (SelectedMatch == null)
                    return null;
                if (tracks == null&&metaData!=null)
                    tracks = metaData.GetTracks();
                return tracks;
            }
            public override string GetTitle()
            {
                return SelectedMatch != null ? (metaData?.AlbumMeta?.Album ?? Matches?.ElementAtOrDefault(SelectedMatch??0)?.Album):null;
            }
            public override string GetArtist()
            {
                return SelectedMatch != null ? (metaData?.AlbumMeta?.Artist ?? Matches?.ElementAtOrDefault(SelectedMatch ?? 0)?.Artist):null;
            }


            public override bool AssociateMetaData()
            {
                if (metaData != null)
                    return true;
                if (Matches == null || Matches.Length == 0)
                    return false;
                GD3.AlbumCode match = Matches[SelectedMatch ?? 0];
                var albumCode = Math.Abs(match.AlbumCode1);
                return metaDataGD3.cdCodeToMetaData.TryGetValue(albumCode, out metaData);
            }

            public override async Task<bool> RetrieveMetaData()
            {
                if (Matches == null || Matches.Length == 0 || metaData != null)
                    return false;
                GD3.AlbumCode match = Matches[SelectedMatch ?? 0];
                var albumCode1 = match.AlbumCode1;
                if (AssociateMetaData() || metaDataGD3.authCD == null)
                    return false;
                var task = metaDataGD3.SoapClientCD.RetrieveAlbumAsync(metaDataGD3.authCD, albumCode1, 0);
                if (await Task.WhenAny(task, Task.Delay(30000)) != task)
                    throw new Exception("GD3 RetrieveAlbumAsync timeout: " + albumCode1);
                var retrieveAlbumResponse = await task;
                var albumMeta = retrieveAlbumResponse?.RetrieveAlbumResult;
                if (albumMeta == null)
                    return false;
                var albumCode = albumMeta.AlbumID;
                if (albumCode == 0)
                    albumCode = albumMeta.AlbumCode;
                if (albumCode == 0)
                    albumCode = Math.Abs(albumCode1);
                string fileNameArtist = MetaDataProvider.RemoveBlacklistedCharacters(albumMeta.Artist ?? "ArtistUnk", 40);
                string fileNameAlbum = MetaDataProvider.RemoveBlacklistedCharacters(albumMeta.Album ?? "AlbumUnk", 80);
                string fileNameBase = $"Meta_CD_{fileNameArtist}_{fileNameAlbum}_{albumCode}";
                string albumImageFileName = null;
                var ai = albumMeta.AlbumImage;
                if (ai != null)
                {
                    if (ai.Length > 0)
                        albumImageFileName = await WriteImage(ai, metaDataGD3.GD3CDPath, fileNameBase);
                    albumMeta.AlbumImage = null;
                }
                var m = new MetaDataCD
                {
                    AlbumCode = albumCode,
                    AlbumMeta = albumMeta,
                    ImageFileName = albumImageFileName
                };
                using (var f = File.Create(Path.Combine(metaDataGD3.GD3CDPath, Path.ChangeExtension(fileNameBase, "json"))))
                {
                    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                    JsonSerializer.Serialize(w, m);
                    f.Close();
                }
                metaDataGD3.cdCodeToMetaData[albumCode] = m;
                metaData = m;
                return true;
            }
        }
        public abstract class MatchDVDBase : Match
        {
            public string[] FrontCoverImages { get; set; }
            public GD3DVD.DVDMatch[] Matches { get; set; }
            private MetaDataDVD metaData;
            public override bool HasMetaData() => metaData != null;
            public abstract string RelPath();
            public override string GetArtFileURL()
            {
                if (SelectedMatch == null)
                    return null;
                var fn = metaData?.ImageFileName;
                if (fn == null && SelectedMatch.HasValue && FrontCoverImages != null)
                    fn = FrontCoverImages[SelectedMatch.Value];
                return fn != null ? RelPath() + '/' + HttpUtility.UrlEncode(fn) : null;
            }
            public override string GetPlot() { return SelectedMatch != null ? (metaData?.DVDMeta?.Plot):null; }

            static string GetTitlePlusDisc(GD3DVD.DVDMeta dvdMeta)
            {
                if (dvdMeta != null)
                {
                    string titlePlusDisc = dvdMeta.Title;
                    if (titlePlusDisc != null)
                    {
                        if (dvdMeta.DiscsTotal > 1)
                        {
                            string dd = dvdMeta.DiscDescription;
                            if (String.IsNullOrEmpty(dd))
                                dd = dvdMeta.DiscNumber.ToString();
                            titlePlusDisc += $"({dd}of{dvdMeta.DiscsTotal})";
                        }
                        return titlePlusDisc;
                    }
                }
                return null;
            }
            public override string GetTitle()
            {
                return SelectedMatch!=null?(GetTitlePlusDisc(metaData?.DVDMeta)??Matches?.ElementAtOrDefault(SelectedMatch??0)?.DVDTitle):null;
            }

            public override string GetArtist()
            {
                return SelectedMatch != null ? (metaData?.DVDMeta?.Studios?.ElementAtOrDefault(0)?.StudioName):null;
            }

            public override bool AssociateMetaData()
            {
                if (metaData != null)
                    return true;
                if (Matches == null || Matches.Length == 0)
                    return false;
                GD3DVD.DVDMatch match = Matches[SelectedMatch ?? 0];
                var dvdCode = match.DVDCode;
                var discID = match.DiscID;
                var t = Tuple.Create(dvdCode, discID);
                return metaDataGD3.dvdCodeDiscIDToMetaData.TryGetValue(t, out metaData);
            }

            public async Task<bool> RetrieveMetaData(MetaDataGD3 metaDataGD3, string path)
            {
                if (Matches == null || Matches.Length == 0 || metaData != null)
                    return false;
                GD3DVD.DVDMatch match = Matches[SelectedMatch ?? 0];
                var dvdCode = match.DVDCode;
                var discID = match.DiscID;
                var t = Tuple.Create(dvdCode, discID);
                if (AssociateMetaData())
                    return false;
                var task = metaDataGD3.SoapClientDVD.RetrieveDVDMetaByDiscIDAsync(metaDataGD3.authDVD, dvdCode, discID);
                if (await Task.WhenAny(task, Task.Delay(30000)) != task)
                    throw new Exception("GD3DVD RetrieveDVDMetaByDiscIDAsync timeout: " + dvdCode);
                var retrieveDVDMetaResponse = await task;
                var dvdMeta = retrieveDVDMetaResponse?.RetrieveDVDMetaByDiscIDResult;
                if (dvdMeta == null)
                    return false;
                string title = MetaDataProvider.RemoveBlacklistedCharacters(GetTitlePlusDisc(dvdMeta) ?? "NoTitle", 80);
                string fileNameBase = $"Meta_{match.MediaType}_{title}_{dvdCode}_{discID}";
                string frontCoverImageFileName = null;
                string backCoverImageFileName = null;
                var ai = dvdMeta.FrontCover;
                var bc = dvdMeta.BackCover;

                if (ai != null)
                {
                    if (ai.Length > 0)
                    {
                        frontCoverImageFileName = await WriteImage(ai, path, $"MetaFrontCover_{title}_{dvdCode}");
                    }
                    dvdMeta.FrontCover = null;
                }
                if (bc != null)
                {
                    if (bc.Length > 0)
                    {
                        backCoverImageFileName = await WriteImage(bc, path, $"MetaBackCover_{title}_{dvdCode}");
                    }
                    dvdMeta.BackCover = null;
                }
                var m = new MetaDataDVD
                {
                    DVDCode = dvdCode,
                    DiscID = discID,
                    DVDMeta = dvdMeta,
                    ImageFileName = frontCoverImageFileName ?? backCoverImageFileName
                };
                using (var f = File.Create(Path.Combine(path, Path.ChangeExtension(fileNameBase, "json"))))
                {
                    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                    JsonSerializer.Serialize(w, m);
                    f.Close();
                }
                metaDataGD3.dvdCodeDiscIDToMetaData[t] = m;
                metaData = m;
                return true;
            }

        }
        public class MatchDVD : MatchDVDBase
        {
            public MatchDVD() { }
            public MatchDVD(MetaDataGD3 m) { metaDataGD3 = m; }
            public override async Task<bool> RetrieveMetaData()
            {
                return await RetrieveMetaData(metaDataGD3, metaDataGD3.GD3DVDPath);
            }
            public override string RelPath() { return metaDataGD3.GD3DVDRelPath; }
            public string GraceNoteDiscID { get; set; }
        }

        public class MatchBD : MatchDVDBase
        {
            public MatchBD() { }
            public MatchBD(MetaDataGD3 m) { metaDataGD3 = m; }

            public override async Task<bool> RetrieveMetaData()
            {
                return await RetrieveMetaData(metaDataGD3, metaDataGD3.GD3BDPath);
            }
            public override string RelPath() { return metaDataGD3.GD3BDRelPath; }

            public byte[] AACSDiscID { get; set; }
        }

        private ConcurrentDictionary<string, MatchCD> nameToMatchCD = new ConcurrentDictionary<string, MatchCD>();
        private ConcurrentDictionary<string, MatchDVD> nameToMatchDVD = new ConcurrentDictionary<string, MatchDVD>();
        private ConcurrentDictionary<string, MatchBD> nameToMatchBD = new ConcurrentDictionary<string, MatchBD>();
        private ConcurrentDictionary<int[], string> lengths2NameCD = new ConcurrentDictionary<int[], string>(new MetaDataProvider.IntArrayComparer());
        private ConcurrentDictionary<string, string> graceNoteID2NameDVD = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<byte[], string> AACSDiscID2NameBD = new ConcurrentDictionary<byte[], string>(new MetaDataProvider.ByteArrayComparer());
        private ConcurrentDictionary<int, MetaDataCD> cdCodeToMetaData = new ConcurrentDictionary<int, MetaDataCD>();
        private ConcurrentDictionary<Tuple<int, int>, MetaDataDVD> dvdCodeDiscIDToMetaData = new ConcurrentDictionary<Tuple<int, int>, MetaDataDVD>();
        private string settingsJsonFileName;

        public class Settings
        {
            public string UserName { get; set; }
            public string Password { get; set; }
            public bool AutoCDLookup { get; set; }
            public bool AutoDVDLookup { get; set; }
            public bool AutoBDLookup { get; set; }
        }
        private Settings settings;
        public Settings GetSettings() { return settings; }
        public void SaveSettings()
        {
            using (var f = File.Create(settingsJsonFileName))
            {
                var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                JsonSerializer.Serialize(w, settings);
                f.Close();
            }
        }
        private IDataProtector _protector;
        public void SetCredentials(string userName, string passWord)
        {
            if (!String.IsNullOrEmpty(userName) && !String.IsNullOrEmpty(passWord))
            {
                settings.UserName = _protector.Protect(userName);
                settings.Password = _protector.Protect(passWord);
                authCD = new GD3.AuthHeader() { Username = userName, Password = passWord };
                authDVD = new GD3DVD.AuthHeader() { Username = userName, Password = passWord };
            }
            else
            {
                settings.UserName = null;settings.Password = null;authCD = null;authDVD = null;
            }
            UpdateLookupsRemaining();
            SaveSettings();
        }

        private GD3.AuthHeader authCD;
        private GD3DVD.AuthHeader authDVD;
        private GD3.GD3SoapClient SoapClientCD = new GD3.GD3SoapClient(GD3.GD3SoapClient.EndpointConfiguration.GD3Soap);
        private GD3DVD.GD3DVDSoapClient SoapClientDVD = new GD3DVD.GD3DVDSoapClient(GD3DVD.GD3DVDSoapClient.EndpointConfiguration.GD3DVDSoap);
        public class LookupsRemaining
        {
            public int? CD, DVD;
            public string ErrorCD, ErrorDVD;
        }
        public LookupsRemaining CurrentLookupsRemaining;
        public async Task<LookupsRemaining> GetNumberLookupsRemainingAsync(GD3.AuthHeader ahCD, GD3DVD.AuthHeader ahDVD)
        {
            var l = new LookupsRemaining();
            try
            {
                var r = await SoapClientCD.GetNumberOfLookupsRemainingAsync(ahCD);
                l.CD = r.GetNumberOfLookupsRemainingResult;
            }
            catch (Exception e)
            {
                l.ErrorCD = e.Message;
            }
            try
            {
                var r = await SoapClientDVD.GetNumberOfLookupsRemainingAsync(ahDVD);
                l.DVD = r.GetNumberOfLookupsRemainingResult;
            }
            catch (Exception e)
            {
                l.ErrorDVD = e.Message;
            }
            return l;
        }

        public async void UpdateLookupsRemaining()
        {
            if (authCD == null && authDVD == null)
                CurrentLookupsRemaining = null;
            else
            {
                var l = await GetNumberLookupsRemainingAsync(authCD, authDVD);
                CurrentLookupsRemaining = l;
            }
        }

        public MetaDataGD3(IDataProtectionProvider provider, string contentRootPath, string GD3Path, string GD3RelPath)
        {
            _protector = provider.CreateProtector("DiscChanger.NET.GD3");

            settingsJsonFileName = Path.Combine(contentRootPath, "GD3settings.json");
            try
            {
                settings = File.Exists(settingsJsonFileName) ? JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(settingsJsonFileName)) : new Settings();
                if (settings?.UserName != null && settings?.Password != null)
                {
                    var u = _protector.Unprotect(settings.UserName);
                    var p = _protector.Unprotect(settings.Password);
                    authCD = new GD3.AuthHeader() { Username = u, Password = p };
                    authDVD = new GD3DVD.AuthHeader() { Username = u, Password = p };
                    UpdateLookupsRemaining();
                }
            }
            catch(Exception e)
            {
                if (settings == null)
                    settings = new Settings();
                System.Diagnostics.Debug.WriteLine($"Error {e.Message} reading or processing GD3 settings from: {settingsJsonFileName}");
            }
            this.GD3Path = GD3Path;
            this.GD3CDPath = Path.Combine(GD3Path, "CD");
            var dirGD3CD = Directory.CreateDirectory(GD3CDPath);
            this.GD3DVDPath = Path.Combine(GD3Path, "DVD");
            var dirGD3DVD = Directory.CreateDirectory(GD3DVDPath);
            this.GD3BDPath = Path.Combine(GD3Path, "BD");
            var dirGD3BD = Directory.CreateDirectory(GD3BDPath);
            this.GD3RelPath = GD3RelPath;
            this.GD3CDRelPath = GD3RelPath + "/CD";
            this.GD3DVDRelPath = GD3RelPath + "/DVD";
            this.GD3BDRelPath = GD3RelPath + "/BD";

            foreach (var fileInfo in dirGD3CD.GetFiles("Match_*.json"))
            {
                MatchCD m = JsonSerializer.Deserialize<MatchCD>(File.ReadAllText(fileInfo.FullName));
                m.SetMetaDataGD3(this);
                string baseName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                nameToMatchCD[baseName] = m;
                lengths2NameCD[m.Lengths] = baseName;
            }
            foreach (var fileInfo in dirGD3DVD.GetFiles("Match_*.json"))
            {
                MatchDVD m = JsonSerializer.Deserialize<MatchDVD>(File.ReadAllText(fileInfo.FullName));
                m.SetMetaDataGD3(this);
                string baseName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                nameToMatchDVD[baseName] = m;
                if (!String.IsNullOrEmpty(m.GraceNoteDiscID))
                    graceNoteID2NameDVD[m.GraceNoteDiscID] = baseName;
            }
            foreach (var fileInfo in dirGD3BD.GetFiles("Match_*.json"))
            {
                MatchBD m = JsonSerializer.Deserialize<MatchBD>(File.ReadAllText(fileInfo.FullName));
                m.SetMetaDataGD3(this);
                string baseName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                nameToMatchBD[baseName] = m;
                if (m.AACSDiscID != null && m.AACSDiscID.Length > 0)
                    AACSDiscID2NameBD[m.AACSDiscID] = baseName;
            }
            foreach (var fileInfo in dirGD3CD.GetFiles("Meta_*.json"))
            {
                MetaDataCD m = JsonSerializer.Deserialize<MetaDataCD>(File.ReadAllText(fileInfo.FullName));
                cdCodeToMetaData[m.AlbumCode] = m;
            }
            foreach (var fileInfo in dirGD3DVD.GetFiles("Meta_*.json").Concat(dirGD3BD.GetFiles("Meta_*.json")))
            {
                MetaDataDVD m = JsonSerializer.Deserialize<MetaDataDVD>(File.ReadAllText(fileInfo.FullName));
                dvdCodeDiscIDToMetaData[Tuple.Create(m.DVDCode, m.DiscID)] = m;
            }
        }


        public bool AutoLookupEnabled(Disc d)
        {
            return (settings.AutoCDLookup && d.IsCD()) ||
                   (settings.AutoDVDLookup && d.IsDVD()) ||
                   (settings.AutoBDLookup && d.IsBD());
        }

        static string TableOfContents2GTOC(DiscSony.TOC toc)
        {
            StringBuilder sb = new StringBuilder(";");
            foreach (var title in toc.Titles)
            {
                sb.Append(title); sb.Append("|||:");
                bool first = true;
                foreach (var chapterFrame in toc.TitleFrames[title])
                {
                    if (!first)
                        sb.Append(' ');
                    else
                        first = false;
                    sb.Append(chapterFrame);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        internal Match Get(Disc d)
        {
            Match r = null;
            if (d.IsCD())
            {
                var lengths = d.StandardizedCDTableOfContents();
                if (lengths != null && lengths2NameCD.TryGetValue(lengths, out string name))
                    r = nameToMatchCD[name];
            }
            else if (d.IsDVD() && d is DiscSony ds)
            {
                var gtoc = (ds as DiscSonyBD)?.DiscIDData?.GraceNoteDiscID ?? TableOfContents2GTOC(ds.TableOfContents);
                if (gtoc != null && graceNoteID2NameDVD.TryGetValue(gtoc, out string name))
                    r = nameToMatchDVD[name];
            }
            else if (d.IsBD() && d is DiscSonyBD dbd)
            {
                var AACSDiscID = dbd.DiscIDData.AACSDiscID;
                if (AACSDiscID != null && AACSDiscID.Length > 0 && AACSDiscID2NameBD.TryGetValue(AACSDiscID, out string name))
                    r = nameToMatchBD[name];
            }
            if (r != null)
                r.AssociateMetaData();
            return r;
        }
        static public string MakeUniqueFileName(IDictionary d, string baseName)
        {
            string fileName = baseName;
            int i = 1;
            while (d.Contains(fileName))
            {
                fileName = baseName + "_(" + i.ToString() + ')'; i++;
            }
            return fileName;
        }
        internal async Task<Match> RetrieveMatch(Disc d)
        {
            if (d is DiscSony ds)
            {
                if (ds.IsCD())
                {
                    var lengths = d.StandardizedCDTableOfContents();
                    if (lengths != null)
                    {
                        if (lengths2NameCD.TryGetValue(lengths, out string name))
                            return nameToMatchCD[name];
                        if (authCD == null)
                            return null;
                        int frameCount = lengths.Length;
                        string graceNoteDiscID = (ds as DiscSonyBD)?.DiscIDData?.GraceNoteDiscID;
                        string cdID;
                        if (String.IsNullOrEmpty(graceNoteDiscID))
                        {
                            graceNoteDiscID = null;
                            int initial = 150;
                            var cumulative = lengths.Aggregate(new List<int>(frameCount + 1) { initial }, (c, nxt) => { c.Add(c.Last() + nxt); return c; });
                            cdID = String.Join(' ', cumulative);
                        }
                        else
                            cdID = graceNoteDiscID;

                        var task = SoapClientCD.MatchCDIDAsync(authCD, cdID);
                        if (await Task.WhenAny(task, Task.Delay(30000)) != task)
                            throw new Exception("GD3 MatchCDIDAsync timeout: " + cdID);
                        var matchCDIDResponse = await task;
                        var matchesCD = matchCDIDResponse?.MatchCDIDResult;
                        GD3.AlbumCode[] matchesCDNonNull;
                        if (matchesCD != null && (matchesCDNonNull = matchesCD.Where(m => m != null).ToArray()) != null && matchesCDNonNull.Length > 0)
                        {
                            var matchCD = new MatchCD(this)
                            {
                                SelectedMatch = 0,
                                GraceNoteDiscID = graceNoteDiscID,
                                Lengths = lengths,
                                Matches = matchesCDNonNull                                
                            };
                            var firstMatch = matchesCDNonNull.First();
                            string fileNameArtist = MetaDataProvider.RemoveBlacklistedCharacters(firstMatch.Artist ?? "ArtistUnk", 40);
                            string fileNameAlbum = MetaDataProvider.RemoveBlacklistedCharacters(firstMatch.Album ?? "TitleUnk", 80);
                            string fileName = MakeUniqueFileName(nameToMatchCD, $"Match_CD_{fileNameArtist}_{fileNameAlbum}_{firstMatch.MatchType}");
                            using (var f = File.Create(Path.Combine(GD3CDPath, Path.ChangeExtension(fileName, "json"))))
                            {
                                var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                                JsonSerializer.Serialize(w, matchCD);
                                f.Close();
                            }
                            nameToMatchCD[fileName] = matchCD;
                            lengths2NameCD[lengths] = fileName;
                            matchCD.AssociateMetaData();
                            return matchCD;
                        }
                    }
                }
                else if (ds.IsDVD() || ds.IsBD())
                {
                    MatchDVDBase match = null;
                    GD3DVD.DVDMatch[] matches = null;
                    IDictionary nameToMatch = null;
                    string path = null;
                    if (ds.IsDVD())
                    {
                        string gtoc = (ds as DiscSonyBD)?.DiscIDData?.GraceNoteDiscID;
                        if (gtoc == null)
                            gtoc = TableOfContents2GTOC(ds.TableOfContents);
                        if (String.IsNullOrEmpty(gtoc))
                            return null;
                        if (graceNoteID2NameDVD.TryGetValue(gtoc, out string name))
                            return nameToMatchDVD[name];
                        if (authDVD == null)
                            return null;
                        var task = SoapClientDVD.MatchDVDID_vToc2Async(authDVD, gtoc);
                        if (await Task.WhenAny(task, Task.Delay(30000)) != task)
                            throw new Exception("GD3 MatchDVDID_vToc2Async timeout: " + gtoc);
                        var matchDVDID_vToc2Response = await task;
                        matches = matchDVDID_vToc2Response?.MatchDVDID_vToc2Result?.DVDMatches;
                        path = GD3DVDPath;
                        match = new MatchDVD(this)
                        {
                            SelectedMatch = 0,
                            GraceNoteDiscID = gtoc
                        };
                        nameToMatch = nameToMatchDVD;
                    }
                    else if (ds.IsBD() && d is DiscSonyBD bdd)
                    {
                        var AACSDiscID = bdd.DiscIDData?.AACSDiscID;
                        if (AACSDiscID == null || AACSDiscID.Length == 0)
                            return null;
                        if (AACSDiscID2NameBD.TryGetValue(AACSDiscID, out string name))
                            return nameToMatchBD[name];
                        if (authDVD == null)
                            return null;
                        var task = SoapClientDVD.Match_AACSAsync(authDVD, bdd.DiscIDData.AACSDiscID);
                        if (await Task.WhenAny(task, Task.Delay(30000)) != task)
                            throw new Exception("GD3 Match_AACSAsync timeout");
                        var match_AACSResponse = await task;
                        matches = match_AACSResponse?.Match_AACSResult?.DVDMatches;
                        path = GD3BDPath;
                        match = new MatchBD(this)
                        {
                            SelectedMatch = 0,
                            AACSDiscID = AACSDiscID
                        };
                        nameToMatch = nameToMatchBD;
                    }
                    GD3DVD.DVDMatch[] matchesNonNull;
                    if (matches != null && (matchesNonNull = matches.Where(m => m != null).ToArray()).Length > 0)
                    {
                        string[] frontCoverImages = new string[matchesNonNull.Length];
                        int i = 0;
                        foreach (var m in matchesNonNull)
                        {
                            string mediaType2 = MetaDataProvider.RemoveBlacklistedCharacters(m.MediaType ?? "NoMediaType", 11);
                            string title2 = MetaDataProvider.RemoveBlacklistedCharacters(m.DVDTitle ?? "NoTitle", 80);

                            var frontCoverImage = m.FrontCoverImage;
                            if (frontCoverImage != null && frontCoverImage.Length > 0)
                            {
                                var frontCoverImageFileName = await WriteImage(frontCoverImage, path, $"MatchFrontCover_{mediaType2}_{title2}_{m.DVDCode}_{m.DiscID}");
                                frontCoverImages[i] = frontCoverImageFileName;
                                match.FrontCoverImages = frontCoverImages;
                                m.FrontCoverImage = null;//erase base64-encoded thumbnail to save space
                            }
                            var extraImages = m.ExtraImages;
                            if (extraImages != null)
                            {
                                foreach (var ei in extraImages)
                                {
                                    try
                                    {
                                        var task2 = SoapClientDVD.RetrieveExtraMovieImageAsync(authDVD, ei.MovieImageID, false);

                                        if (await Task.WhenAny(task2, Task.Delay(30000)) != task2)
                                            throw new Exception("GD3 RetrieveExtraMovieImageAsync timeout: " + ei.MovieImageID);
                                        var retrieveExtraMovieImageResponse = await task2;
                                        byte[] b = retrieveExtraMovieImageResponse?.RetrieveExtraMovieImageResult;
                                        if (b != null && b.Length > 0)
                                        {
                                            var movieImageFileName = await WriteImage(b, path, $"MovieImage_{ei.MovieImageID}");
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Error retrieving Movie Image {ei.MovieImageID}: {e.Message}");
                                    }
                                }
                            }
                            i++;
                        }

                        match.Matches = matchesNonNull;
                        var firstMatch = matchesNonNull.First();
                        string mediaType = MetaDataProvider.RemoveBlacklistedCharacters(firstMatch.MediaType ?? "NoMediaType", 11);
                        string title = MetaDataProvider.RemoveBlacklistedCharacters(firstMatch.DVDTitle ?? "NoTitle", 80);
                        string fileName = MakeUniqueFileName(nameToMatch, $"Match_{mediaType}_{title}_{firstMatch.MatchType}");
                        using (var f = File.Create(Path.Combine(path, Path.ChangeExtension(fileName, "json"))))
                        {
                            var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                            JsonSerializer.Serialize(w, (object)match);
                            f.Close();
                        }
                        if (match is MatchDVD matchDVD)
                        {
                            nameToMatchDVD[fileName] = matchDVD;
                            graceNoteID2NameDVD[matchDVD.GraceNoteDiscID] = fileName;
                        }
                        if (match is MatchBD matchBD)
                        {
                            nameToMatchBD[fileName] = matchBD;
                            AACSDiscID2NameBD[matchBD.AACSDiscID] = fileName;
                        }
                        match.AssociateMetaData();
                        return match;
                    }
                }
            }
            return null;
        }
    }
}
