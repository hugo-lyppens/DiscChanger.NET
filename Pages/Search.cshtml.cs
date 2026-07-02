/*  Copyright 2020 Hugo Lyppens

    Search.cshtml.cs is part of DiscChanger.NET.

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
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using DiscChanger.Models;

namespace DiscChanger.Pages
{
    public class SearchModel : PageModel
    {
        private readonly ILogger<SearchModel> _logger;
        public DiscChangerService discChangerService;

        public string Query { get; set; } = string.Empty;
        public List<SearchResult> Results { get; set; } = new();

        public SearchModel(DiscChangerService discChangerService, ILogger<SearchModel> logger)
        {
            this.discChangerService = discChangerService;
            _logger = logger;
        }

        public void OnGet(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return;

            Query = q.Trim();
            string queryLower = Query.ToLowerInvariant();

            foreach (var dc in discChangerService.DiscChangers)
            {
                var discs = dc.Discs;
                if (discs == null) continue;

                List<KeyValuePair<string, Disc>> discList;
                lock (discs)
                {
                    discList = discs.ToList();
                }

                foreach (var kvp in discList)
                {
                    var disc = kvp.Value;
                    if (disc == null) continue;

                    string slot   = disc.Slot ?? string.Empty;
                    string artist = disc.GetArtist() ?? string.Empty;
                    string title  = disc.getTitle() ?? string.Empty;
                    var tracks    = disc.GetTracks();

                    // Match slot number exactly
                    bool slotMatch = slot.Equals(Query, StringComparison.OrdinalIgnoreCase);

                    // Match artist or title (case-insensitive contains)
                    bool artistMatch = artist.Contains(queryLower, StringComparison.OrdinalIgnoreCase);
                    bool titleMatch  = title.Contains(queryLower, StringComparison.OrdinalIgnoreCase);

                    // Match any track title
                    string matchedTrack = null;
                    if (tracks != null)
                    {
                        var trackMatch = tracks.FirstOrDefault(t =>
                            t.Title != null &&
                            t.Title.Contains(queryLower, StringComparison.OrdinalIgnoreCase));
                        matchedTrack = trackMatch?.Title;
                    }

                    if (slotMatch || artistMatch || titleMatch || matchedTrack != null)
                    {
                        string matchType = slotMatch   ? "Slot"   :
                                           artistMatch ? "Artist" :
                                           titleMatch  ? "Title"  : "Track";

                        Results.Add(new SearchResult
                        {
                            ChangerName  = dc.Name,
                            ChangerKey   = dc.Key,
                            Slot         = slot,
                            Artist       = artist,
                            Title        = title,
                            ArtUrl       = disc.DataGD3Match?.GetArtFileURL() ?? disc.DataMusicBrainz?.GetArtFileURL(),
                            MatchType    = matchType,
                            MatchedTrack = matchedTrack
                        });
                    }
                }
            }

            // Sort: slot matches first, then artist, then title, then track
            Results.Sort((a, b) =>
            {
                int order(string t) => t switch { "Slot" => 0, "Artist" => 1, "Title" => 2, _ => 3 };
                int cmp = order(a.MatchType).CompareTo(order(b.MatchType));
                if (cmp != 0) return cmp;
                // Within same match type, sort by slot number
                if (int.TryParse(a.Slot, out int sa) && int.TryParse(b.Slot, out int sb))
                    return sa.CompareTo(sb);
                return string.Compare(a.Slot, b.Slot, StringComparison.OrdinalIgnoreCase);
            });
        }

        public class SearchResult
        {
            public string ChangerName  { get; set; }
            public string ChangerKey   { get; set; }
            public string Slot         { get; set; }
            public string Artist       { get; set; }
            public string Title        { get; set; }
            public string ArtUrl       { get; set; }
            public string MatchType    { get; set; }
            public string MatchedTrack { get; set; }
        }
    }
}
