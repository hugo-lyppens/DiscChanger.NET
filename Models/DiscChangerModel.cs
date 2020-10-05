﻿/*  Copyright 2020 Hugo Lyppens

    DiscChangerModel.cs is part of DiscChanger.NET.

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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.IO.Ports;
using System.Collections.Concurrent;
using DiscChanger.Hubs;
using System.Collections;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace DiscChanger.Models
{
    public class DiscChangerModel: IDisposable
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }
        //public string Protocol { get; set; }
        public string Connection { get; set; }
        public string CommandMode { get; set; }
        public string PortName { get; set; }
        public bool AdjustLastTrackLength { get; set; } = true;
        public bool ReverseDiscExistBytes { get; set; } = false;
        private ILogger<DiscChangerService> logger;
        private IHubContext<DiscChangerHub> hubContext;
        private SerialPort serialPort;
        static Dictionary<string, byte> CommandMode2PDC = new Dictionary<string, byte> { { "BD1", (byte)0x80 }, { "BD2", (byte)0x81 }, { "BD3", (byte)0x82 } };
        static Dictionary<string, byte> CommandMode2ResponsePDC = new Dictionary<string, byte> { { "BD1", (byte)0x88 }, { "BD2", (byte)0x89 }, { "BD3", (byte)0x8A } };
        byte PDC=0xD0;
        byte ResponsePDC=0xD8;
        public static byte[] BitReverseTable =
    {
    0x00, 0x80, 0x40, 0xc0, 0x20, 0xa0, 0x60, 0xe0,
    0x10, 0x90, 0x50, 0xd0, 0x30, 0xb0, 0x70, 0xf0,
    0x08, 0x88, 0x48, 0xc8, 0x28, 0xa8, 0x68, 0xe8,
    0x18, 0x98, 0x58, 0xd8, 0x38, 0xb8, 0x78, 0xf8,
    0x04, 0x84, 0x44, 0xc4, 0x24, 0xa4, 0x64, 0xe4,
    0x14, 0x94, 0x54, 0xd4, 0x34, 0xb4, 0x74, 0xf4,
    0x0c, 0x8c, 0x4c, 0xcc, 0x2c, 0xac, 0x6c, 0xec,
    0x1c, 0x9c, 0x5c, 0xdc, 0x3c, 0xbc, 0x7c, 0xfc,
    0x02, 0x82, 0x42, 0xc2, 0x22, 0xa2, 0x62, 0xe2,
    0x12, 0x92, 0x52, 0xd2, 0x32, 0xb2, 0x72, 0xf2,
    0x0a, 0x8a, 0x4a, 0xca, 0x2a, 0xaa, 0x6a, 0xea,
    0x1a, 0x9a, 0x5a, 0xda, 0x3a, 0xba, 0x7a, 0xfa,
    0x06, 0x86, 0x46, 0xc6, 0x26, 0xa6, 0x66, 0xe6,
    0x16, 0x96, 0x56, 0xd6, 0x36, 0xb6, 0x76, 0xf6,
    0x0e, 0x8e, 0x4e, 0xce, 0x2e, 0xae, 0x6e, 0xee,
    0x1e, 0x9e, 0x5e, 0xde, 0x3e, 0xbe, 0x7e, 0xfe,
    0x01, 0x81, 0x41, 0xc1, 0x21, 0xa1, 0x61, 0xe1,
    0x11, 0x91, 0x51, 0xd1, 0x31, 0xb1, 0x71, 0xf1,
    0x09, 0x89, 0x49, 0xc9, 0x29, 0xa9, 0x69, 0xe9,
    0x19, 0x99, 0x59, 0xd9, 0x39, 0xb9, 0x79, 0xf9,
    0x05, 0x85, 0x45, 0xc5, 0x25, 0xa5, 0x65, 0xe5,
    0x15, 0x95, 0x55, 0xd5, 0x35, 0xb5, 0x75, 0xf5,
    0x0d, 0x8d, 0x4d, 0xcd, 0x2d, 0xad, 0x6d, 0xed,
    0x1d, 0x9d, 0x5d, 0xdd, 0x3d, 0xbd, 0x7d, 0xfd,
    0x03, 0x83, 0x43, 0xc3, 0x23, 0xa3, 0x63, 0xe3,
    0x13, 0x93, 0x53, 0xd3, 0x33, 0xb3, 0x73, 0xf3,
    0x0b, 0x8b, 0x4b, 0xcb, 0x2b, 0xab, 0x6b, 0xeb,
    0x1b, 0x9b, 0x5b, 0xdb, 0x3b, 0xbb, 0x7b, 0xfb,
    0x07, 0x87, 0x47, 0xc7, 0x27, 0xa7, 0x67, 0xe7,
    0x17, 0x97, 0x57, 0xd7, 0x37, 0xb7, 0x77, 0xf7,
    0x0f, 0x8f, 0x4f, 0xcf, 0x2f, 0xaf, 0x6f, 0xef,
    0x1f, 0x9f, 0x5f, 0xdf, 0x3f, 0xbf, 0x7f, 0xff
};
        internal BitArray getDiscsPresent()
        {
            byte[] discExistBitReq = new byte[] { PDC, 0x8C };
            SendCommand(discExistBitReq);
            List<byte> discExistBit = new List<byte>(50);
            byte[] b; byte count = 0;
            do
            {
                if (!this.responses.TryTake(out b, 10000))
                    break;
                var l = b.Length;
                if (l > 2)
                {
                    if (b[0] == ResponsePDC && b[1] == 0x8C)
                    {
                        byte packetNo = b[2];
                        if (packetNo != count)
                        {
                            string e = "Unexpected packet number " + packetNo + " instead of " + count + "in DISC_EXIST_BIT from " + GetBytesToString(discExistBitReq);
                            System.Diagnostics.Debug.WriteLine(e);
                            throw new Exception(e);
                        }
                        if (this.ReverseDiscExistBytes)
                        {
                            for (int i = 3; i < l; i++)
                                b[i] = BitReverseTable[b[i]];
                        }
                        discExistBit.AddRange(b.Skip(3));
                        count++;
                    }
                    else
                        break;
                }
            }
            while (count < 5);

            System.Diagnostics.Debug.WriteLine("Received " + count + " packets from " + GetBytesToString(discExistBitReq));
            return new BitArray(discExistBit.ToArray());
        }
        internal string getDiscsToScan()
        {
            BitArray discExistBitArray = getDiscsPresent();
            foreach( var key in this.Discs.Keys)
            {
                if( Int32.TryParse( key, out int position))
                {
                    discExistBitArray.Set(position - 1, false);
                }
            }
            return toSetString(discExistBitArray);
        }


        internal string getDiscsToDelete()
        {
            BitArray discExistBitArray = getDiscsPresent();
            var l = discExistBitArray.Length;
            BitArray discToDeleteBitArray = new BitArray(l);
            foreach (var key in this.Discs.Keys)
            {
                if (Int32.TryParse(key, out int position) && position<=l && !discExistBitArray.Get(position-1))
                {
                    discToDeleteBitArray.Set(position - 1, true);
                }
            }
            return toSetString(discToDeleteBitArray);
        }

        public static IEnumerable<int> ParseInterval(string interval)
        {
            var a = interval.Split('-');
            var start = Int32.Parse(a[0]);
            switch (a.Length)
            {
                case 1: return new int[] { start };
                case 2: return Enumerable.Range(start, Int32.Parse(a[1]) + 1 - start);
                default: throw new Exception("Interval syntax error: " + interval);
            }
        }
        public static IEnumerable<int> ParseSet( string set)
        {
            var a = set.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return a.SelectMany(s => ParseInterval(s));
        }
        public CancellationTokenSource ScanCancellationTokenSource;
        public class ScanStatus
        {
            public int? DiscNumber;
            public int? Index;
            public int? Count;
        }
        public ScanStatus currentScanStatus;
        public async Task Scan(string discSet)
        {
            try
            {
                await hubContext.Clients.All.SendAsync("ScanInProgress", Key, true);
                ScanCancellationTokenSource = new CancellationTokenSource();

                var discList = ParseSet(discSet).ToList();
                discList.Sort();
                var ct = ScanCancellationTokenSource.Token;
                this.discNumberBufferBlock = new BufferBlock<int>();
                this.currentScanStatus = new ScanStatus();
                int discNumber = 0;
                int count = discList.Count();
                int index = 0;
                foreach (var disc in discList)
                {
                    ++index;
                    if (ct.IsCancellationRequested)
                        break;
                    if (disc <= discNumber)
                        continue;
                    var s = DiscDirect(disc, null, null, 0x01);
                    if (s == "NACK")
                        continue;
                    currentScanStatus.DiscNumber = disc;
                    currentScanStatus.Index = index;
                    currentScanStatus.Count = count;
                    await hubContext.Clients.All.SendAsync("ScanStatus",
                                                       Key,
                                                       disc,
                                                       index,
                                                       count);
                    var task = discNumberBufferBlock.OutputAvailableAsync(ct);
                    if (await Task.WhenAny(task, Task.Delay(60000, ct)) == task)
                    {
                        await task;
                    }
                    else
                    {
                        ScanCancellationTokenSource.Cancel();
                        return;
                    }
                    discNumber = discNumberBufferBlock.Receive();
                }
                discNumberBufferBlock.Complete();
                ScanCancellationTokenSource.Dispose();
            }
            finally
            {
                discNumberBufferBlock = null;
                ScanCancellationTokenSource = null;
                await hubContext.Clients.All.SendAsync("ScanInProgress", Key, false);
                currentScanStatus = null; 
            }
        }
        public void CancelScan()
        {
            if (ScanCancellationTokenSource == null)
                throw new Exception("There is no scan in progress on " + this.Key);
            ScanCancellationTokenSource.Cancel();
        }
        public ScanStatus getScanStatus() { return currentScanStatus; }

        internal async Task DeleteDiscs(string discSet)
        {
            if (ScanCancellationTokenSource != null)
                throw new Exception("There is a scan in progress on " + this.Key);
            bool b = false;

            lock (this.Discs)
            {
                foreach(var disc in ParseSet(discSet))
                {
                    b |= this.Discs.TryRemove(disc.ToString(), out Disc d);
                }
            }
            if (b)
                this.discChangerService.needsSaving = true;
            await hubContext.Clients.All.SendAsync("Reload");
        }

        private string toSetString(BitArray discExistBitArray)
        {
            StringBuilder sb = new StringBuilder();
            int l = discExistBitArray.Length;
            bool v = false;
            int start=0;
            for(int i=0; i<=l; i++)
            {
                bool b = i<l && discExistBitArray.Get(i);
                if (b == v)
                    continue;
                if (b && !v)
                    start = i+1;
                else
                {
                    if (sb.Length > 0)
                        sb.Append(',');
                    sb.Append(start);
                    if (i > start)
                    {
                        sb.Append('-');
                        sb.Append(i);
                    }
                }
                v = b;
            }
            return sb.ToString();
        }
        public bool SupportsCommand(string command)
        {
            return this.Type==DiscChangerService.BDP_CX7000ES||command!="open";
        }
        internal async Task<string> Test()
        {
            StringBuilder sb = new StringBuilder();        
            
            sb.AppendLine("Power OK: " + await Control("power_on"));
            byte[] b = ProcessPacketCommand(new byte[] { PDC, 0xA0 });//Model_name_req
            var i = Array.IndexOf(b, (byte)0, 2);
            var count = i != -1 ? i - 2 : b.Length - 2;
            var modelName= System.Text.Encoding.ASCII.GetString(b, 2, count);
            sb.Append("Model name=");sb.AppendLine(modelName);
            b = ProcessPacketCommand(new byte[] { PDC, 0x80 });//cis_command_version_req
            sb.AppendLine($"System Control Version={b[2]}.{b[3]}, Interface Control Version={b[4]}.{b[5]}");
            sb.AppendLine(currentStatus.ToString());
            return sb.ToString();
        }

        static public readonly System.Text.Encoding iso_8859_1 = System.Text.Encoding.GetEncoding("iso-8859-1");
        internal void Disconnect()
        {
            if (this.serialPort != null && this.serialPort.IsOpen) { serialPort.Close(); }; serialPort?.Dispose(); serialPort = null;
        }

    public ConcurrentDictionary<string, Disc> Discs = new ConcurrentDictionary<string, Disc>();
        public List<Disc> DiscList { get; set; }
        public bool? HardwareFlowControl { get; set; }

        DiscChangerService discChangerService;
        public DiscChangerModel() : this(null) { }
        //{
        //    _timer = new Timer(DoWork);
        //}
        public DiscChangerModel(string key) { Key = key; _timer = new Timer(processNewDisc); }
        BlockingCollection<byte[]> responses = new BlockingCollection<byte[]>();

        public static Comparer<KeyValuePair<string, Disc>> NumberTextComparer = Comparer<KeyValuePair<string, Disc>>.Create((p1, p2) =>
          {
              if (Int32.TryParse(p1.Key, out int v1) && Int32.TryParse(p2.Key, out int v2))
                  return v1.CompareTo(v2);
              else
                  return p1.Key.CompareTo(p1.Key);
          });

        public struct Status
        {
            public int? discNumber;
            public int? titleAlbumNumber;
            public int? chapterTrackNumber;
            public byte status;
            public string statusString;
            public byte mode;
            public string modeDisc;
            public byte modeBroadCast;
            public byte setupLock;
            public override string ToString()
            {
                return $"Status Data: {discNumber}/{titleAlbumNumber}/{chapterTrackNumber}: {statusString}({status}), mode:{Convert.ToString(mode, 2)}, disc_mode:{modeDisc}, broadcast:{modeBroadCast}, setup_lock:{setupLock}";
            }

        };
        //public struct DiscData
        //{
        //    public int? discNumber;
        //    public byte discType;
        //    public int startTrackTitleAlbum;
        //    public int lastTrackTitleAlbum;
        //    public byte addedInformation;
        //};
        //static readonly Dictionary<ushort, string> languageCode2String = new Dictionary<ushort, string> {
        //    { (ushort)0x0000, "DiscMemo" },
        //    { (ushort)0x1144, "English" }
        //};

        //static readonly Dictionary<byte, string> characterCodeSet2String = new Dictionary<byte, string> {
        //    { (byte)0x00, "ISO-646(ASCII)" },
        //    { (byte)0x01, "ISO-8859" },
        //    { (byte)0x10, "Disc Memo(ASCII)" }
        //};
        //public struct TextData
        //{
        //    public int? discNumber;
        //    public int? titleTrackNumber;
        //    public byte type;
        //    public string language;
        //    public string characterSet;
        //    public string text;
        //}

        //class TOC
        //{
        //    public List<int> titles = new List<int>();
        //    public Dictionary<int, List<int>> titleFrames = new Dictionary<int, List<int>>();
        //}
        private Status currentStatus = new Status { statusString = "off" };
        public Status CurrentStatus() 
        {
            return currentStatus; 
        }
        //public static byte[] GetStringToBytes(string value)
        //{
        //    SoapHexBinary shb = SoapHexBinary.Parse(value);
        //    return shb.Value;
        //}

        public static string GetBytesToString(byte[] value)
        {
            return BitConverter.ToString(value);
        }

        private static byte CheckSum(byte[] a)
        {
            int s = 0;
            foreach (byte b in a)
            {
                s = (s + b) & 0xFF;
            }
            return (byte)s;
        }
        private static int? FromBCD(byte h, byte l)
        {
            if (l == 0xFF || h == 0xFF)
                return null;
            return (h >> 4) * 1000 + (h & 0xF) * 100 + (l >> 4) * 10 + (l & 0xF);
        }
        private static void ToBCD(int? i, out byte h, out byte l)
        {
            if (!i.HasValue)
            {
                l = 0xFF; h = 0xFF;
            }
            else
            {
                int v = i.Value;
                int nybbleHH = v / 1000;
                v -= nybbleHH * 1000;
                int nybbleHL = v / 100;
                v -= nybbleHL * 100;
                int nybbleLH = v / 10;
                v -= nybbleLH * 10;
                h = (byte)((nybbleHH << 4) | nybbleHL);
                l = (byte)((nybbleLH << 4) | v);
            }
        }
        private static int? FromBCD(byte b)
        {
            if (b == 0xFF)
                return null;
            return (b >> 4) * 10 + (b & 0xF);
        }
        private void SendCommand(byte[] command, bool clearSerial = true)
        {
            if (serialPort == null)
                throw new Exception("Serial port not open: " + this.Key + ',' + this.PortName);
            lock (serialPort)
            {
                if (clearSerial)
                {
                    serialPort.DiscardInBuffer();
                    while (responses.TryTake(out _)) { }
                }
                int l = command.Length;
                byte[] header = new byte[2] { 2, (byte)l };
                byte[] trailer = new byte[1] { (byte)((-l - CheckSum(command)) & 0xFF) };
                byte[] combined = header.Concat(command).Concat(trailer).ToArray();
                serialPort.Write(combined, 0, combined.Length);
            }
        }
        private byte[] ReadByteOrPacket()
        {
            int b = serialPort.ReadByte();
            if (b == 2)
            {
                int l = serialPort.ReadByte();
                byte[] buffer = new byte[l];
                int i = 0;
                while (i < l)
                {
                    int n = serialPort.Read(buffer, i, l - i);
                    i += n;
                }
                int checkSum = (l + serialPort.ReadByte() + DiscChangerModel.CheckSum(buffer)) & 0xFF;
                return checkSum == 0 ? buffer : null;
            }
            return new byte[1] { (byte)b };
        }
        private byte[] ReadPacket()
        {
            for (; ; )
            {
                byte[] b = ReadByteOrPacket();
                if (b.Length > 1)
                    return b;
                System.Diagnostics.Debug.WriteLine("Byte Received: " + b[0].ToString("X"));
                this.responses.Add(b);
            }
        }

        //private  byte[] processCommand(SerialPort serialPort, byte[] command)
        //{
        //    sendCommand(command);
        //    try
        //    {
        //        return ReadByteOrPacket();
        //    }
        //    catch (TimeoutException)
        //    {
        //        return null;
        //    }
        //}
        private byte[] ProcessPacketCommand(byte[] command)
        {
            SendCommand(command);
            byte[] b; int count = 0;
            do
            {
                if (!this.responses.TryTake(out b, 2000))
                {
                    string err = "Timout waiting for response packet from " + GetBytesToString(command);
                    System.Diagnostics.Debug.WriteLine(err);
                    throw new Exception(err);
                }
                count++;
            }
            while ((b.Length < 2 || b[0] != this.ResponsePDC || b[1] != command[1]) && count < 10);
            if (count == 10)
            {
                string err="Did not get response to packet command: " + GetBytesToString(command);
                System.Diagnostics.Debug.WriteLine(err);
                throw new Exception(err);
            }
            return b;
        }
        private bool ProcessAckCommand(byte[] command)
        {
            SendCommand(command);
            byte[] b; int count = 0;
            do
            {
                if (!this.responses.TryTake(out b, 3000))
                {
                    System.Diagnostics.Debug.WriteLine("Timout waiting for ACK/NACK from " + GetBytesToString(command));
                    throw new Exception("Timout waiting for ACK/NACK from " + GetBytesToString(command));
                }
                count++;
            }
            while (b.Length != 1 && count < 10);
            if(b.Length==1)
            {
                if (b[0] == ACK)
                    return true;
                else if (b[0] == NACK)
                    return false;
            }
            System.Diagnostics.Debug.WriteLine("Received unexpected response from " + GetBytesToString(command) + ": " + GetBytesToString(b));
            throw new Exception("Received unexpected response from " + GetBytesToString(command) + ": " + GetBytesToString(b));
        }
        private  bool ProcessAckCommand(byte command)
        {
            return ProcessAckCommand(new byte[] { PDC, command });
        }
        static readonly byte ACK = 0xFD;
        static readonly byte NACK = 0xFE;
        static readonly Dictionary<byte, string> ack2String = new Dictionary<byte, string> {
            { ACK, "ACK" },
            { NACK, "NACK" }
        };

        static readonly Dictionary<byte, string> statusCode2String = new Dictionary<byte, string> {
            { (byte)0x00, "off" },
            { (byte)0x10, "stop" },
            { (byte)0x11, "play" },
            { (byte)0x12, "pause" },
            { (byte)0x20, "load" },
            { (byte)0x30, "open" },
            { (byte)0xee, "error" },
            { (byte)0xff, "other" }
        };
        static readonly Dictionary<string, byte> commandString2Code = new Dictionary<string, byte> {
            { "disc_skip_next", (byte)0x0a},
            { "disc_skip_previous", (byte)0X0b},
            { "folder", (byte)0x0c},
            { "audio", (byte)0x0d},
            { "subtitle", (byte)0x0e},
            { "angle", (byte)0x0f},
            { "previous", (byte)0x10},
            { "next", (byte)0x11},
            { "play", (byte)0x12},
            { "pause", (byte)0x13},
            { "stop", (byte)0x14},
            { "time_text", (byte)0x1d},
            { "discs", (byte)0x1e},
            { "rev_scan", (byte)0x20},
            { "fwd_scan", (byte)0x21},
            { "open", (byte)0x39}
        };

        //0x00	Numeric Key 0
        //0x01	Numeric Key 1
        //0x02	Numeric Key 2
        //0x03	Numeric Key 3
        //0x04	Numeric Key 4
        //0x05	Numeric Key 5
        //0x06	Numeric Key 6
        //0x07	Numeric Key 7
        //0x08	Numeric Key 8
        //0x09	Numeric Key 9
        //0x0a	DISC SKIP+
        //0X0b	DISC SKIP-
        //0x0c	FOLDER
        //0x0d	AUDIO
        //0x0e	SUBTITLE
        //0x0f	ANGLE
        //0x10	PREVIOUS
        //0x11	NEXT
        //0x12	PLAY
        //0x13	PAUSE
        //0x14	STOP
        //0x15	CURSOR UP
        //0x16	CURSOR LEFT
        //0x17	ENTER
        //0x18	CURSOR RIGHT
        //0x19	CURSOR DOWN
        //0x1a	TOP MENU
        //0x1b	MENU
        //0x1c	RETURN
        //0x1d	TIME/TEXT
        //0x1e	1/ALL DISCS
        //0x20	REV SCAN/SLOW
        //0x21	RWD SCAN/SLOW
        //0x22	Clear
        //0x44	SETUP_LOCK_SET
        //0x4a	DISC_DIRECT_SET
        //0x60	POWER_SET
        //0x61	BAUD_RATE_SET
        //0x62	BROADCAST_MODE_SET
        //0x63	PARENTAL_CONTROL_RELEASE
        private BufferBlock<int> discNumberBufferBlock;
        private void processNewDisc(object state)
        {
            if (newDisc != null && newDiscNumber != null)
            {
                var dn = newDiscNumber.Value;
                var dns = dn.ToString();
                var newDisc2 = newDisc;
                newDisc2.Slot = dns;
                newDisc2.DiscChanger = this;
                newDisc = null; newDiscNumber = null;
                if (discNumberBufferBlock != null)
                    discNumberBufferBlock.Post(dn);

                if (discChangerService!=null && newDisc2.DiscData?.DiscType != Disc.DiscTypeNone &&(!Discs.TryGetValue(dns, out Disc d)|| (d != newDisc2)))
                    discChangerService.AddDiscData(newDisc2);
            }
            newDisc=null; newDiscNumber=null;
        }
        System.Threading.Timer _timer;
        Disc newDisc;
        int? newDiscNumber;
        private bool disposedValue;

        private void DataReceivedHandler(
                            object sender,
                            SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            //int count = sp.BytesToRead;
            //if (count > 0)
            //{
            //    byte[] buffer = new byte[count];
            //    int n = sp.Read(buffer, 0, count);
            //    System.Diagnostics.Debug.WriteLine("Received: " + GetBytesToString(buffer));
            //}

            try
            {
                lock (sp)
                {
                    int count;
                    while ((count = sp.BytesToRead) > 0)
                    {
                        byte[] b = ReadByteOrPacket();
                        int l = b.Length;
                        if (b == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Null in DataReceivedHandler");
                        }
                        else if (l == 1)
                        {
                            System.Diagnostics.Debug.WriteLine("Byte Received: " + b[0].ToString("X"));
                            this.responses.Add(b);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Packet Received: " + GetBytesToString(b));
                            if (b[0] == ResponsePDC && l >= 2)
                            {
                                int? discNumber;
                                byte cmd = b[1];
                                switch (cmd)
                                {
                                    case 0x82://STATUS_DATA
                                        currentStatus.discNumber = FromBCD(b[2], b[3]);
                                        currentStatus.titleAlbumNumber = FromBCD(b[4], b[5]);
                                        currentStatus.chapterTrackNumber = FromBCD(b[6], b[7]);
                                        currentStatus.status = b[8];
                                        currentStatus.statusString = statusCode2String[currentStatus.status];
                                        currentStatus.mode = b[9];
                                        currentStatus.modeDisc = (currentStatus.mode & 32) != 0 ? "all" : "one";
                                        currentStatus.modeBroadCast = (byte)((currentStatus.mode >> 2) & 3);
                                        currentStatus.setupLock = (byte)(currentStatus.mode & 1);

                                        //                                        string msg5 ="Status: " + (currentStatus.discNumber??-1) + "/" + (currentStatus.titleAlbumNumber ?? -1) + "/" + (currentStatus.chapterTrackNumber ?? -1) + ":" + currentStatus.status + "," + Convert.ToString(currentStatus.mode, 2);
                                        string msg5 = "Status: " + currentStatus.ToString();

                                        System.Diagnostics.Debug.WriteLine(msg5);
                                        //                                        _hubContext.Clients.All.SendAsync("ReceiveMessage", "test", msg5);
                                        hubContext?.Clients.All.SendAsync("StatusData",
                                                                           Key,
                                                                           currentStatus.discNumber,
                                                                           currentStatus.titleAlbumNumber,
                                                                           currentStatus.chapterTrackNumber,
                                                                           currentStatus.statusString,
                                                                           currentStatus.modeDisc);
                                        break;
                                    case 0x8A://DISC_DATA
                                        Disc.Data dd = new Disc.Data();
                                        discNumber = FromBCD(b[2], b[3]);
                                        byte discTypeByte = b[4];
                                        dd.DiscType = Disc.discType2String[discTypeByte];
                                        dd.StartTrackTitleAlbum = FromBCD(b[5], b[6]);
                                        dd.LastTrackTitleAlbum = FromBCD(b[7], b[8]);
                                        dd.HasMemo = (b[9] & 1) != 0;
                                        dd.HasText = (b[9] & 16) != 0;
                                        string msg4 ="DiscData: " + (discNumber ?? -1) + "/" + (dd.StartTrackTitleAlbum ?? -1) + "/" + (dd.LastTrackTitleAlbum ?? -1) + ":" + dd.DiscType + "," + Convert.ToString(b[9], 2);
                                        System.Diagnostics.Debug.WriteLine(msg4);
                                        if (discNumber.HasValue )
                                        {
                                            if (newDisc == null || newDiscNumber != discNumber)
                                            {
                                                newDiscNumber = discNumber;
                                                newDisc = new Disc(); 
                                                _timer.Change(TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
                                            }
                                            newDisc.DiscData = dd;
                                            if (newDisc.HasAll())
                                                _timer.Change(TimeSpan.FromSeconds(0), Timeout.InfiniteTimeSpan);
                                        }
                                        break;
                                    case 0x90://TEXT_DATA
                                        discNumber = FromBCD(b[2], b[3]);
                                        int? titleTrackNumber = FromBCD(b[4]); //reserved 0x00
                                        byte type = b[5]; //reserved 0x00
                                        string characterSet = Disc.characterCodeSet2String[b[8]];
                                        byte expectedPacketNumber = 0;
                                        //                                        IEnumerable<byte> text = Enumerable.Empty<byte>();
                                        List<byte> text = new List<byte>();
                                        bool error = false;
                                        do
                                        {
                                            byte packetNumber = b[9];
                                            if (packetNumber != expectedPacketNumber || FromBCD(b[2], b[3]) != discNumber)
                                            {
                                                error = true; break;
                                            }
                                            if (b.Last() == (byte)0x00)
                                            {
                                                int i = b.Length - 2;
                                                while (i >= 10 && b[i] == (byte)0x00)
                                                    i--;
                                                text.AddRange(b.Skip(10).Take(i-9));
                                                break;
                                            }
                                            text.AddRange(b.Skip(10));
                                            expectedPacketNumber++; b = ReadPacket(); l = b.Length;
                                        }
                                        while (!error);                                       
                                        Disc.Text td = new Disc.Text();
                                        string msg = "TEXT_DATA: " + discNumber;

                                        var textArray = text.ToArray();
                                        if (textArray.Length > 0)
                                        {
                                            td.Language = Disc.languageCode2String[(ushort)((b[6] << 8) | b[7])];
                                            td.TextString = (characterSet == "ISO-8859" ? iso_8859_1 : System.Text.Encoding.
                                                ASCII).GetString(textArray);
                                            msg += ":" + td.Language + "," + characterSet + ":" + td.TextString;
                                        }
                                        else
                                        {
                                            msg += ": blank";
                                        }
                                        System.Diagnostics.Debug.WriteLine(msg);
                                        if (discNumber.HasValue)
                                        {
                                            if (newDisc == null || newDiscNumber != discNumber || newDisc.DiscText != null)
                                            {
                                                System.Diagnostics.Debug.WriteLine("Got unexpected TEXT_DATA: " + discNumber.Value + ":" + (td.Language ?? "NullLanguage") + "/" + (td.TextString ?? "NullText"));
                                            }
                                            else
                                            {
                                                newDisc.DiscText = td;
                                                if (newDisc.HasAll())
                                                    _timer.Change(TimeSpan.FromSeconds(0), Timeout.InfiniteTimeSpan);
                                            }
                                        }

                                        //                                        hubContext.Clients.All.SendAsync("ReceiveMessage", "test", msg);
                                        break;
                                    case 0x8B://TOC_DATA
                                        List<string> Titles = new List<string>();
                                        ConcurrentDictionary<string, List<int>> TitleFrames = new ConcurrentDictionary<string, List<int>>();
                                        byte mode;
                                        do
                                        {
                                            discNumber = FromBCD(b[2], b[3]);
                                            //                                            int? titleNumber = fromBCD(b[4]);
                                            byte titleNumber = b[4]; //bcd 0x00-0x99 or 0xCD or 0xFF;
                                            mode = b[l - 1];
                                            if (titleNumber != 0xFF)
                                            {
                                                string titleNumberStr = titleNumber != 0xCD ? FromBCD(titleNumber).Value.ToString() : "CD";
                                                if (!TitleFrames.TryGetValue(titleNumberStr, out List<int> frames))
                                                {
                                                    Titles.Add(titleNumberStr);
                                                    frames = new List<int>();
                                                    TitleFrames[titleNumberStr]=frames;
                                                }
                                                int i;
                                                for (i = 5; i + 3 < l; i += 4)
                                                {
                                                    byte msb = b[i];
                                                    int sign = ((msb >> 7) == 0) ? 1 : -1;
                                                    msb = (byte)(msb & 0x7F);
                                                    frames.Add(sign * (FromBCD(msb, b[i + 1]).Value * 10000 + FromBCD(b[i + 2], b[i + 3]).Value));
                                                }
                                                System.Diagnostics.Debug.WriteLine("Frames: " + String.Join(",", frames.Select(f => f.ToString())) + ", mode:" + mode.ToString("X") + "(" + i + "," + l + ")");
                                            }
                                            switch (mode)
                                            {
                                                case 0xCC:
                                                    System.Diagnostics.Debug.WriteLine("TOC data continues into next packet");
                                                    b = ReadPacket();l = b.Length;
                                                    break;
                                                case 0xEE:
                                                    System.Diagnostics.Debug.WriteLine("TOC data end of title");
                                                    b = ReadPacket(); l = b.Length;
                                                    break;
                                                case 0xFF:
                                                    System.Diagnostics.Debug.WriteLine("TOC data end of disc");
                                                    break;
                                                default:
                                                    throw new Exception("Unknown mode: " + mode.ToString("X"));
                                            }
                                        } while (mode != 0xff);
    
                                        Disc.TOC toc = new Disc.TOC();
                                        toc.Titles = Titles.ToArray();
                                        toc.TitleFrames = new ConcurrentDictionary<string, int[]>(
                                            TitleFrames.Select(kvp => new KeyValuePair<string, int[]>(kvp.Key, kvp.Value.ToArray())));

                                        if (discNumber.HasValue)
                                        {
                                            if (newDisc == null )
                                            {
                                                newDiscNumber = discNumber;
                                                newDisc = new Disc();
                                                _timer.Change(TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
                                            }

                                            if (newDiscNumber != discNumber || newDisc.TableOfContents != null)
                                            {
                                                System.Diagnostics.Debug.WriteLine("Got unexpected TOC_DATA: " + discNumber.Value+(newDiscNumber?.ToString()??"NullNewDiscNum")+":"+ (newDisc.TableOfContents != null));
                                            }
                                            else
                                            {
                                                newDisc.TableOfContents = toc;
                                                if (newDisc.HasAll())
                                                    _timer.Change(TimeSpan.FromSeconds(0), Timeout.InfiniteTimeSpan);
                                            }
                                        }
                                        break;
                                    default:
                                        this.responses.Add(b);
                                        //                                        System.Diagnostics.Debug.WriteLine("Unrecognized:" + cmd.ToString("X"));
                                        break;

                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Exception: " + ex.ToString());
            }
        }

        private void OpenSerial()
        {
            System.Diagnostics.Debug.WriteLine("Opening serial port: " + PortName);
            try
            {
                serialPort = new SerialPort(PortName);

                serialPort.BaudRate = 9600;
                serialPort.Parity = Parity.None;
                serialPort.StopBits = StopBits.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = HardwareFlowControl==true? Handshake.RequestToSend:Handshake.None;
                serialPort.RtsEnable = HardwareFlowControl==true;
                serialPort.ReadTimeout = 5000;
                serialPort.WriteTimeout = 10000;
                serialPort.Open();
                serialPort.DataReceived += DataReceivedHandler;
                //ProcessAckCommand(new byte[] { PDC, 0x62, 0x02 });
            }
            catch
            {
                try
                {
                    serialPort?.Close();
                }
                catch { };
                serialPort = null;
                throw;
            }
        }

        public void Connect()
        {
            OpenSerial();
            try
            {
                ProcessAckCommand(new byte[] { PDC, 0x62, 0x02 });
                SendCommand(new byte[] { PDC, 0x82 });
            }
            catch { }
        }
        public void Connect(DiscChangerService discChangerService, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger)
        {
            this.logger = logger;
            this.hubContext = hubContext;
            this.discChangerService = discChangerService;
            PDC = (Type == DiscChangerService.BDP_CX7000ES) ? CommandMode2PDC[CommandMode] : (byte)0xD0;
            ResponsePDC = (Type == DiscChangerService.BDP_CX7000ES) ? CommandMode2ResponsePDC[CommandMode] : (byte)0xD8;
            Connect();
        }
        public async Task<string> Control(string command)
        {
            if (command.StartsWith("power"))
            {
                if (serialPort == null)
                    OpenSerial();
                byte desiredState = command == "power_on" || (command == "power" && currentStatus.status == 0) ? (byte)1 : (byte)0;
                var powerCommand = new byte[] { PDC, 0x60, desiredState };
                int iterations = desiredState * 9 + 1;
                int i;
                bool success = false;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                string r = null;
                for (i = 0; !success && i < iterations; i++)
                {
                    SendCommand(powerCommand, i == 0);
                    sw.Start();
                    if (responses.TryTake(out byte[] response, 200))
                    {
                        while (response.Length > 1)
                        {
                            System.Diagnostics.Debug.WriteLine(command + " attempt: " + i + ", skipping large response packet:" + GetBytesToString(response));
                            if (!responses.TryTake(out response))
                                break;
                        }
                        if (response.Length == 1)
                        {
                            success = true; r = ack2String[response[0]];
                        }
                        while (!success) ;
                    }
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine(command + " attempt: " + i + ", ms:" + sw.ElapsedMilliseconds);
                }
                if (!success)
                {
                    throw new Exception("unexpected power response");
                }
                if (desiredState == 0x01)
                {
                    bool? ack = null;
                    for (i = 0; ack != true && i < 30; i++)
                    {
                        try
                        {
                            ack = ProcessAckCommand(new byte[] { PDC, 0x62, 0x02 });
                            await Task.Delay(2000);
                        }
                        catch { }
                    }
                    if (ack != true)
                        throw new Exception("could not set broadcast mode");
                    System.Diagnostics.Debug.WriteLine($"It took {i} attempts to set broadcasting mode");
                }
                return r;
            }
            else
            {
                byte commandCode = commandString2Code[command];
                return ProcessAckCommand(commandCode) ? "ACK" : "NACK";
            }
        }
        public string DiscDirect(int? discNumber, int? titleAlbumNumber, int? chapterTrackNumber, byte control=0x00)
        {
            ToBCD(discNumber ?? 0, out byte discNumberH, out byte discNumberL);
            byte trackTitleNumberH, trackTitleNumberL;
            byte chapterNumberH, chapterNumberL;
            if( titleAlbumNumber!=null )
            {
                ToBCD(titleAlbumNumber ?? 0,   out trackTitleNumberH, out trackTitleNumberL);
                ToBCD(chapterTrackNumber ?? 0, out chapterNumberH,    out chapterNumberL);
            }
            else
            {
                ToBCD(chapterTrackNumber ?? 0, out trackTitleNumberH, out trackTitleNumberL);
                chapterNumberH = 0; chapterNumberL = 0;
            }
//            byte control = pause? 0x01:0x00;//Play, 0x01 for pause
            var discDirectSetCommand = new byte[] { PDC, 0x4A, discNumberH, discNumberL, trackTitleNumberH, trackTitleNumberL, chapterNumberH, chapterNumberL, control };
            return ProcessAckCommand(discDirectSetCommand) ? "ACK" : "NACK";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    ScanCancellationTokenSource?.Dispose();ScanCancellationTokenSource = null;
                    Disconnect();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
