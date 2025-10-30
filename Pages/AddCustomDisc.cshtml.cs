using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Hosting;
using DiscChanger.Models;

namespace DiscChanger.Pages
{
    public class AddCustomDiscModel : PageModel
    {
        private readonly CustomDiscService customDiscService;
        private readonly DiscChangerService discChangerService;
        private readonly IWebHostEnvironment webHostEnvironment;

        public AddCustomDiscModel(CustomDiscService customDiscService, DiscChangerService discChangerService, IWebHostEnvironment webHostEnvironment)
        {
            this.customDiscService = customDiscService;
            this.discChangerService = discChangerService;
            this.webHostEnvironment = webHostEnvironment;
        }

        [BindProperty(SupportsGet = true)]
        public string ChangerKey { get; set; }

        [BindProperty]
        public string Slot { get; set; }

        [BindProperty]
        public string DiscType { get; set; }

        [BindProperty]
        public string Artist { get; set; }

        [BindProperty]
        public string Title { get; set; }

        [BindProperty]
        public IFormFile CoverArt { get; set; }

        [BindProperty]
        public List<TrackInfo> Tracks { get; set; }

        [BindProperty]
        public string OriginalSlot { get; set; }

        public string ExistingCoverArtUrl { get; set; }
        public bool IsEditMode { get; set; }

        public async Task<IActionResult> OnGetAsync(string changerKey, string slot)
        {
            ChangerKey = changerKey;
            Slot = slot;

            var changer = discChangerService.Changer(ChangerKey);
            if (changer == null)
            {
                ModelState.AddModelError("", "Invalid changer");
                return RedirectToPage("Index");
            }

            // Check if we're editing an existing custom disc
            var existingCustomDisc = customDiscService.GetCustomDisc(ChangerKey, Slot);
            if (existingCustomDisc != null)
            {
                IsEditMode = true;
                OriginalSlot = Slot; // Track original slot for moves
                Artist = existingCustomDisc.Artist;
                Title = existingCustomDisc.Title;
                DiscType = existingCustomDisc.DiscType;

                if (!string.IsNullOrEmpty(existingCustomDisc.CoverArtFileName))
                {
                    ExistingCoverArtUrl = "/CustomCoverArt/" + System.Web.HttpUtility.UrlEncode(existingCustomDisc.CoverArtFileName);
                }

                // Load tracks
                if (existingCustomDisc.Tracks != null)
                {
                    Tracks = existingCustomDisc.Tracks.ToList();
                }
            }
            // Check if editing a scanned disc (copy data from it)
            else if (!string.IsNullOrEmpty(Slot) && changer.Discs.TryGetValue(Slot, out Disc scannedDisc))
            {
                IsEditMode = true;
                OriginalSlot = Slot; // Track original slot for moves
                Artist = scannedDisc.GetArtist();
                Title = scannedDisc.getTitle();
                DiscType = scannedDisc.GetDiscType();

                // Load existing cover art from scanned disc
                if (scannedDisc.DataGD3Match?.GetArtFileURL() != null)
                {
                    ExistingCoverArtUrl = scannedDisc.DataGD3Match.GetArtFileURL();
                }
                else if (scannedDisc.DataMusicBrainz?.GetArtFileURL() != null)
                {
                    ExistingCoverArtUrl = scannedDisc.DataMusicBrainz.GetArtFileURL();
                }

                // Load tracks from scanned disc
                var existingTracks = scannedDisc.GetTracks();
                if (existingTracks != null)
                {
                    Tracks = existingTracks.Select(t => new TrackInfo
                    {
                        Position = t.Position ?? 0,
                        Title = t.Title,
                        Length = t.Length?.ToString(@"h\:mm\:ss") ?? ""
                    }).ToList();
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string op)
        {
            if (op == "cancel")
            {
                return RedirectToPage("Index");
            }

            if (!ModelState.IsValid || string.IsNullOrEmpty(Title))
            {
                return Page();
            }

            var changer = discChangerService.Changer(ChangerKey);
            if (changer == null)
            {
                ModelState.AddModelError("", "Invalid changer");
                return Page();
            }

            // Auto-assign slot if not provided
            if (string.IsNullOrEmpty(Slot))
            {
                var availableSlots = changer.GetAvailableSlots();
                var slots = DiscChanger.Models.DiscChanger.ParseSet(availableSlots);
                Slot = slots.First().ToString();
            }
            else
            {
                // Validate slot number
                if (!int.TryParse(Slot, out int slotNum) || slotNum < 1)
                {
                    ModelState.AddModelError("Slot", "Slot must be a positive number");
                    return Page();
                }
                
                // If editing and slot changed, check if new slot is available
                if (!string.IsNullOrEmpty(OriginalSlot) && OriginalSlot != Slot)
                {
                    // Check if target slot has a custom disc
                    if (customDiscService.GetCustomDisc(ChangerKey, Slot) != null)
                    {
                        ModelState.AddModelError("Slot", $"Slot {Slot} is already occupied by another custom disc.");
                        return Page();
                    }
                    // Check if target slot has a scanned disc
                    if (changer.Discs.ContainsKey(Slot))
                    {
                        ModelState.AddModelError("Slot", $"Slot {Slot} is already occupied by a scanned disc.");
                        return Page();
                    }
                }
            }

            // Create custom disc
            var customDisc = new CustomDisc
            {
                ChangerKey = ChangerKey,
                Slot = Slot,
                DiscType = DiscType ?? "CD",
                Artist = Artist,
                Title = Title,
                DateTimeAdded = DateTime.Now
            };

            // Handle cover art upload
            if (CoverArt != null && CoverArt.Length > 0)
            {
                var coverArtDir = Path.Combine(webHostEnvironment.WebRootPath, "CustomCoverArt");
                if (!Directory.Exists(coverArtDir))
                {
                    Directory.CreateDirectory(coverArtDir);
                }

                var extension = Path.GetExtension(CoverArt.FileName);
                var fileName = $"{ChangerKey}_{Slot}_{DateTime.Now.Ticks}{extension}";
                var filePath = Path.Combine(coverArtDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await CoverArt.CopyToAsync(stream);
                }
                customDisc.CoverArtFileName = fileName;
            }
            else
            {
                // Preserve existing cover art filename if editing
                var slotToCheck = !string.IsNullOrEmpty(OriginalSlot) ? OriginalSlot : Slot;
                var existingCustomDisc = customDiscService.GetCustomDisc(ChangerKey, slotToCheck);
                if (existingCustomDisc != null && !string.IsNullOrEmpty(existingCustomDisc.CoverArtFileName))
                {
                    customDisc.CoverArtFileName = existingCustomDisc.CoverArtFileName;
                }
            }

            // Handle tracks - preserve existing tracks if not explicitly editing them
            var hasTrackEdits = Tracks != null && Tracks.Any(t => !string.IsNullOrWhiteSpace(t.Title) || !string.IsNullOrWhiteSpace(t.Length));
            
            if (hasTrackEdits)
            {
                // User provided track data - update tracks
                customDisc.Tracks = new List<TrackInfo>();
                foreach (var track in Tracks)
                {
                    // Only add tracks that have at least a title or length
                    if (!string.IsNullOrWhiteSpace(track.Title) || !string.IsNullOrWhiteSpace(track.Length))
                    {
                        customDisc.Tracks.Add(track);
                    }
                }
            }
            else
            {
                // No track edits provided - preserve existing tracks
                // Check from OriginalSlot if moving, otherwise from current Slot
                var slotToCheck = !string.IsNullOrEmpty(OriginalSlot) ? OriginalSlot : Slot;
                var existingCustomDisc = customDiscService.GetCustomDisc(ChangerKey, slotToCheck);
                if (existingCustomDisc != null && existingCustomDisc.Tracks != null)
                {
                    // Preserve tracks from existing custom disc
                    customDisc.Tracks = existingCustomDisc.Tracks;
                }
                else
                {
                    // Check if there's a regular disc in the changer we're converting from
                    var sourceChanger = discChangerService.Changer(ChangerKey);
                    if (sourceChanger != null && sourceChanger.Discs.TryGetValue(slotToCheck, out Disc existingDisc))
                    {
                        // Get tracks from the original disc and convert to TrackInfo format
                        var originalTracks = existingDisc.GetTracks();
                        if (originalTracks != null)
                        {
                            customDisc.Tracks = originalTracks.Select(t => new TrackInfo
                            {
                                Position = t.Position ?? 0,
                                Title = t.Title ?? "",
                                Length = t.Length?.ToString(@"h\:mm\:ss") ?? ""
                            }).ToList();
                        }
                    }
                }
            }

            // If slot changed, delete from original slot
            if (!string.IsNullOrEmpty(OriginalSlot) && OriginalSlot != Slot)
            {
                await customDiscService.DeleteCustomDisc(ChangerKey, OriginalSlot);
                // Also remove from scanned discs if present
                if (changer.Discs.TryRemove(OriginalSlot, out Disc removedDisc))
                {
                    discChangerService.needsSaving = true;
                }
            }

            // Save to CustomDiscs.json
            await customDiscService.AddOrUpdateCustomDisc(customDisc);

            return RedirectToPage("Index");
        }
    }
}
