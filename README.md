# DiscChanger.NET
ASP.NET Core Solution to manage discs in disc changers
<img src="doc/DiscChanger.NET.png" />

Uses ASP.NET Core 3.1. Manages any number of disc changers. At the moment two types supported via RS-232 serial connection: DVP-CX777ES and BDP-CX7000ES. Does disc data lookups via MusicBrainz. This solution is primarily relevant for music discs. Since ASP.NET Core is cross-platform, this works on Windows as well as Linux. I've tested it on a Raspberry Pi 4, which can control as many changers as it has serial ports. It has 5 real UARTs (#0, #2-#5) and a mini UART. Using the real UARTs, one Raspberry Pi 4 could control 5 changers!

You can scan all or a subset of the discs in the changer to populate the display. Also, when the changer loads a disc, it checks to see whether it is a known disc. If not, DiscChanger.NET will look it up and show it on the grid without reloading the page.

Future enhancements planned:
- Support for CX7000ES via TCP/IP in addition to Serial.
- Support for GD3 lookups
