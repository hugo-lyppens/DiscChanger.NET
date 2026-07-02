# DiscChanger.NET — Search Feature Guide

## 1. What Changed

Three files were added or modified to bring search to DiscChanger.NET:

| File | Change |
|---|---|
| `Pages/Search.cshtml` + `Pages/Search.cshtml.cs` | **New** — a standalone search page that reads your existing disc library (`DiscChangers.json` and the `Discs/` metadata) and returns matches. |
| `Pages/Index.cshtml` | **Modified** — added a search icon to the navbar, plus logic to receive a disc selection from the Search page, scroll to it, highlight it, and optionally play it. |

No existing controllers, the SignalR hub, or JavaScript files were touched. The feature is fully self-contained on top of the existing app.

**Search covers four fields at once**, in one search box:
- Artist
- Album title
- Track title
- Slot number

## 2. Where It Shows Up in the UI

- **Navbar** — a magnifying-glass icon now appears next to the settings gear on the main page.
- **`/Search` page** — a new page with a single search box. Results appear as cards: cover art thumbnail, artist, album title, slot number, and (if you searched a track) the matching track name.
- **Main disc grid** — clicking a search result sends you back to the main page, which:
  - Smooth-scrolls the matching disc to the **center** of the screen
  - Wraps it in a **pulsing red box** with a red tint — 5 pulses over 5 seconds, then it stays highlighted (border + tint) until you scroll, click, or tap elsewhere on the page
  - If you clicked **Play** instead of just viewing, it also sends the play command to the changer once the live connection is ready

## 3. How to Use Search

1. Click the **magnifying glass icon** in the navbar (next to the gear icon).
2. Type a search term — an artist name, album title, track name, or a slot number.
3. Review the results — each card shows the disc's art, artist/title, slot, and matching track if relevant.
4. Click a result:
   - Clicking the **cover art or disc info** → jumps to the main grid and highlights that disc (view only)
   - Clicking **Play** → jumps to the main grid, highlights the disc, and starts playback automatically
5. On the main grid, the highlighted disc is impossible to miss — a solid red border and pulsing red background. It stays highlighted until you interact with the page again.

## 4. Using the Settings Screen to Confirm the Changer Connection

Before search (or anything else) can control real hardware, the changer itself needs to be configured and connected. This is done from the **settings gear icon** on the main page — independent of the search feature, but essential for the "Play" action to actually do anything.

1. Click the **gear icon** in the navbar to open the settings/config panel.
2. If no changer is listed yet, click **Add** to create one. If it already exists, select it to edit.
3. Fill in:
   - **Port** — the serial device path. On Linux this is typically `/dev/ttyUSB0` (or `/dev/ttyACM0` for some adapters); on Windows it's a `COM` port like `COM3`.
   - **Changer type** — select the matching Sony model (e.g. DVP-CX777ES for RS-232-only changers).
4. Click **Test** — this attempts to open the serial port and query the changer directly.
   - **Success** means the app can talk to the hardware — you're good to save.
   - **Failure** usually means one of:
     - Wrong port path (double-check `/dev/ttyUSB0` exists — see below)
     - The changer isn't powered on or the cable isn't connected
     - A Linux permissions issue (see note below)
5. Once Test succeeds, click **OK / Save** to commit the configuration.
6. Back on the main page, the **power button** should now reflect the real state of the changer (lit/on if the changer is powered on). This matters beyond just cosmetics — some UI actions (like populating the "Go" fields when clicking a track in the popover) are gated on this power state being accurately read from the hardware.

### Linux-specific note: serial port permissions

On Linux, USB-serial adapters are owned by the `dialout` group by default. If Test keeps failing even though the adapter shows up in `lsusb`, check:
```bash
groups $USER
```
If `dialout` isn't listed, add yourself to it and log out/back in (group changes require a fresh session):
```bash
sudo usermod -aG dialout $USER
```
Then retry Test in the settings screen.

## 5. Summary

- Search is a fast way to find any disc by artist, album, track, or slot without scrolling the full grid.
- Results connect back to the live grid with a clear visual highlight and optional one-click play.
- The settings screen is where the actual hardware link is configured and verified — search and play only work end-to-end once that connection Tests successfully.
