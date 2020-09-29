# DiscChanger.NET
ASP.NET Core Solution to manage discs in disc changers
<img src="doc/DiscChanger.NET.png" />

Uses ASP.NET Core 3.1. Manages any number of disc changers. At the moment two types supported via RS-232 serial connection: DVP-CX777ES and BDP-CX7000ES. Does disc data lookups via MusicBrainz. This solution is primarily relevant for music discs. Since ASP.NET Core is cross-platform, this works on Windows as well as Linux. 

I've tested it on a Raspberry Pi 4, which can control as many changers as it has serial ports. It has 5 real UARTs (#0, #2-#5) and a mini UART. Using the real UARTs (be sure to use proper 3.3V-RS232 adapters preferably with hardware flow control CTS/RTS), one Raspberry Pi 4 could control 5 changers! <a href="https://www.raspberrypi.org/documentation/configuration/uart.md">Information on Raspberry Pi UARTs (serial ports)</a>.

And on Windows, it runs in IIS with the ASP.NET Core 3.1 runtime installed or from the command line using the dotnet command. MacOS is also supported by ASP.NET Core 3.1.

You can scan all or a subset of the discs in the changer to populate the display. Also, when the changer loads a disc, it checks to see whether it is a known disc. If not, DiscChanger.NET will look it up and show it on the grid without reloading the page. By clicking on a music CD, you get a <a href="doc/DiscChanger.NET Popover audio tracks and links.png">popover</a> showing listing of tracks as well as web links known to MusicBrainz pertaining to the disc.

To get started, go <a href="https://dotnet.microsoft.com/download/dotnet-core/3.1">here</a> to obtain the correct ASP.NET Core runtime for your OS. For the Raspberry Pi 4 running Raspian, I had to download the ARM32 version even though its CPU is 64 bit. Download DiscChanger.NET from the <a href="https://github.com/hugo-lyppens/DiscChanger.NET/releases">release list</a>

Then ensure you have serial ports working and null modem cables to connect to your disc changers. For PCs, I strongly recommend using any unused motherboard serial port headers (enable in BIOS & connect via Slot Plate adapter from 9-pin Serial to 10-pin mobo Header) before adding USB serial ports. My Supermicro board has two DTK serial port headers, which I've used for my cx777es changers, and my cx7000es is connected via a PL2303 USB serial port.

Then ensure your changers are set up for RS232 control on their settings. Install the software and run via the dotnet command line. Navigate to the webpage and click the Add button to add your disc changer. Press the Test button to ensure the connection works and the changer responds to commands. Before running a wholesale disc scan just scan a few discs to ensure it all works well.

After you scanned all your discs, please be sure to make regular backups of the DiscChangers.json file and the Discs directory so you can restore it if needed without needing to rescan all discs.

Future enhancements planned:
- Support for CX7000ES via TCP/IP in addition to Serial.
- Support for <a href="https://www.getdigitaldata.com/GD3.aspx">GD3</a> lookups
- Support for Firewire changers such as XL1B.
