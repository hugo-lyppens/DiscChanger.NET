using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace DiscChanger.Models
{
    /// <summary>
    /// Represents a custom disc entry stored separately from scanned discs
    /// </summary>
    public class CustomDisc
    {
        [JsonPropertyName("ChangerKey")]
        public string ChangerKey { get; set; }

        [JsonPropertyName("Slot")]
        public string Slot { get; set; }

        [JsonPropertyName("DiscType")]
        public string DiscType { get; set; }

        [JsonPropertyName("Artist")]
        public string Artist { get; set; }

        [JsonPropertyName("Title")]
        public string Title { get; set; }

        [JsonPropertyName("CoverArtFileName")]
        public string CoverArtFileName { get; set; }

        [JsonPropertyName("Tracks")]
        public List<TrackInfo> Tracks { get; set; }

        [JsonPropertyName("DateTimeAdded")]
        public DateTime? DateTimeAdded { get; set; }

        public CustomDisc()
        {
            Tracks = new List<TrackInfo>();
        }

        public string ToHtml(string changerName)
        {
            string HtmlEncode(string s) => System.Web.HttpUtility.HtmlEncode(s);
            string HtmlAttributeEncode(string s) => System.Web.HttpUtility.HtmlAttributeEncode(s);

            var afp = !string.IsNullOrEmpty(CoverArtFileName) ? "/CustomCoverArt/" + System.Web.HttpUtility.UrlEncode(CoverArtFileName) : null;
            
            var slotHtml = HtmlEncode(Slot ?? "--");
            var a = HtmlEncode(Artist ?? String.Empty);
            var t = HtmlEncode(Title ?? String.Empty);

            StringBuilder sb = new StringBuilder(@"<div class=""disc"" data-bs-toggle=""popover"" data-bs-html=""true"" data-bs-title=""", 8192);
            sb.Append(HtmlAttributeEncode(a + "/" + t + @"<button class=""btn-close"">"));
            sb.Append(@""" data-bs-content=""");
            StringBuilder content = new StringBuilder(8192);
            
            if (Tracks != null && Tracks.Count > 0)
            {
                content.Append(@"<table class=""tracks"">");
                foreach (var track in Tracks)
                {
                    content.Append(@"<tr><td>");
                    content.Append(track.Position);
                    content.Append(@"</td><td>"); content.Append(HtmlEncode(track.Title ?? "---")); content.Append("</td><td>");
                    content.Append(track.Length ?? "--");
                    content.Append(@"</td></tr>");
                }
                content.Append(@"</table>");
            }

            // Add Go and Edit buttons to content (before encoding) - use single quotes to avoid encoding issues
            content.Append(@"<div class='disc-actions'>");
            content.Append(@"<button class='btn btn-primary btn-sm disc-go-btn' data-changer='");
            content.Append(ChangerKey); content.Append(@"' data-slot='");
            content.Append(slotHtml); content.Append(@"'>Go</button>");
            content.Append(@"<a href='/AddCustomDisc?changerKey=");
            content.Append(ChangerKey); content.Append(@"&slot=");
            content.Append(slotHtml); content.Append(@"' class='btn btn-secondary btn-sm'>Edit</a>");
            content.Append(@"</div>");

            sb.Append(HtmlAttributeEncode(content.ToString()));
            sb.Append(@""" data-changer=""");
            sb.Append(ChangerKey); sb.Append(@""" data-slot="""); sb.Append(slotHtml); 
            sb.Append(@""" data-media-type="""); 
            sb.Append((DiscType != null && DiscType.ToUpper().Contains("CD")) ? "CD" : ((DiscType != null && (DiscType.ToUpper().Contains("DVD") || DiscType.ToUpper().Contains("BD") || DiscType.ToUpper().Contains("BLU"))) ? "MOVIE" : "OTHER")); 
            sb.Append(@""">");

            if (afp != null)
            {
                sb.Append("<img src=\""); sb.Append(afp); sb.Append(@"""/>");
            }
            sb.Append(@"<div class=""artist"">");
            sb.Append(a);
            sb.Append(@"</div><div class=""title"">");
            sb.Append(t);
            sb.Append(@"</div><div class=""disc-header""><span class=""slot"">");
            sb.Append(HtmlEncode(changerName)); sb.Append(':'); sb.Append(slotHtml);
            sb.Append(@"</span><span class=""disc-type"">");
            sb.Append(HtmlEncode(DiscType ?? "-"));
            sb.Append("</span></div>");
            sb.Append(@"</div>");
            return sb.ToString();
        }
    }

    public class TrackInfo
    {
        [JsonPropertyName("Position")]
        public int Position { get; set; }

        [JsonPropertyName("Title")]
        public string Title { get; set; }

        [JsonPropertyName("Length")]
        public string Length { get; set; }
    }

    /// <summary>
    /// Container for all custom discs
    /// </summary>
    public class CustomDiscCollection
    {
        [JsonPropertyName("CustomDiscs")]
        public List<CustomDisc> CustomDiscs { get; set; }

        public CustomDiscCollection()
        {
            CustomDiscs = new List<CustomDisc>();
        }
    }
}

