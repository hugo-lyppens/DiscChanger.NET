/*  Copyright 2020 Hugo Lyppens

    DiscChanger.cs is part of DiscChanger.NET.

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
using System.Text.Json.Serialization;
using System.Net.Sockets;
using System.Net;
using System.Globalization;

namespace DiscChanger.Models
{
    [JsonConverter(typeof(DiscChangerConverter))]
    public abstract class DiscChanger : IDisposable
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Type { get; set; }
        //public string Protocol { get; set; }
        public string Connection { get; set; }
        public string CommandMode { get; set; }
        public string PortName { get; set; }
        public string NetworkHost { get; set; }
        public int? NetworkPort { get; set; }
        protected ILogger<DiscChangerService> logger;
        protected IHubContext<DiscChangerHub> hubContext;
        protected SerialPort serialPort;
        protected Socket networkSocket;
        protected const int NetworkBufferSize = 1;
        protected byte[] networkBuffer;
        public const string CONNECTION_SERIAL_PORT = "SerialPort";
        public const string CONNECTION_NETWORK = "Network";
        public static readonly string[] ConnectionTypes = new string[] { CONNECTION_SERIAL_PORT, CONNECTION_NETWORK };
        public static readonly string[] ChangerTypes = new string[] { String.Empty, DiscChangerSonyDVD.DVP_CX777ES, DiscChangerSonyBD.BDP_CX7000ES };


        protected object CurrentConnection() { return (object)serialPort ?? (object)networkSocket; }
        public virtual bool Connected()
        {
            if (serialPort != null)
                return serialPort.IsOpen;
            else if (networkSocket != null)
                return networkSocket.Connected;
            return false;
        }
        protected void DiscardInBuffer()
        {
            if (serialPort != null)
                serialPort.DiscardInBuffer();
            else if (networkSocket != null)
            {
                int a;
                while ((a = networkSocket.Available) > 0)
                    networkSocket.Receive(new byte[a]);
            }
            else
                throw new Exception("DiscardInBuffer neither serial port nor network connection");
        }

        protected byte ReadByte()
        {
            if (serialPort != null)
            {
                int i = serialPort.ReadByte();
                if (i == -1)
                    throw new Exception("Serial Port ReadByte returned -1");
                return (byte)i;
            }
            if (networkSocket != null)
            {
                byte[] b = new byte[1];
                if (networkSocket.Receive(b) != 1)
                    throw new Exception("Network ReadByte error");
                return b[0];
            }
            throw new Exception("ReadByte neither serial port nor network connection");
        }
        protected int ReadBytes(byte[] buffer, int offset, int size)
        {
            if (serialPort != null)
                return serialPort.Read(buffer, offset, size);
            if (networkSocket != null)
                return networkSocket.Receive(buffer, offset, size, SocketFlags.None);
            throw new Exception("ReadBytes neither serial port nor network connection");
        }
        protected void WriteBytes(byte[] buffer, int offset, int size)
        {
            if (serialPort != null)
                serialPort.Write(buffer, offset, size);
            else if (networkSocket != null)
            {
                int sent = networkSocket.Send(buffer, offset, size, SocketFlags.None);
                if (sent != size)
                    throw new Exception($"Socket.Send returned {sent} instead of {size}");
            }
            else
                throw new Exception("WriteBytes neither serial port nor network connection");
        }

        static public DiscChanger Create(string Type)
        {
            switch (Type)
            {
                case DiscChangerSonyBD.BDP_CX7000ES:
                    return new DiscChangerSonyBD(Type);
                case DiscChangerSonyDVD.DVP_CX777ES:
                    return new DiscChangerSonyDVD(Type);
                default:
                    throw new Exception($"DiscChanger.Create unknown type {Type}");
            }
        }

        internal abstract Type getDiscType();
        internal abstract Disc createDisc(string slot = null);

        internal void setDisc(string slot, Disc d)
        {
            d.DiscChanger = this;
            d.Slot = slot;
            Discs[slot] = d;
        }

        protected Disc newDisc;

        internal abstract BitArray getDiscsPresent();
        internal abstract string getDiscsToScan();

        internal abstract string getDiscsToDelete();

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
        public static IEnumerable<int> ParseSet(string set)
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
        public abstract Task<bool> LoadDisc(int disc);
        public async Task Scan(string discSet)
        {
            try
            {
                await hubContext.Clients.All.SendAsync("ScanInProgress", Key, true);
                ScanCancellationTokenSource = new CancellationTokenSource();

                var discList = ParseSet(discSet).ToList();
                discList.Sort();
                var ct = ScanCancellationTokenSource.Token;
                this.bufferBlockDiscSlot = new BufferBlock<string>();
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
                    {
                        System.Diagnostics.Debug.WriteLine($"Continuing because {disc} <= {discNumber}");
                        continue;
                    }
                    if (!await LoadDisc(disc))
                    {
                        System.Diagnostics.Debug.WriteLine($"Continuing because LoadDisc false {disc}");
                        continue;
                    }
                    currentScanStatus.DiscNumber = disc;
                    currentScanStatus.Index = index;
                    currentScanStatus.Count = count;
                    await hubContext.Clients.All.SendAsync("ScanStatus",
                                                       Key,
                                                       disc,
                                                       index,
                                                       count);
                    var task = bufferBlockDiscSlot.OutputAvailableAsync(ct);
                    if (await Task.WhenAny(task, Task.Delay(180000, ct)) == task)
                    {
                        await task;
                    }
                    else
                    {
                        ScanCancellationTokenSource.Cancel();
                        return;
                    }
                    discNumber = Int32.Parse(bufferBlockDiscSlot.Receive());//for now assume numeric slots only
                    System.Diagnostics.Debug.WriteLine($"bufferBlockDiscSlot.Receive {discNumber}");

                }
                bufferBlockDiscSlot.Complete();
                ScanCancellationTokenSource.Dispose();
            }
            finally
            {
                bufferBlockDiscSlot = null;
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
                foreach (var disc in ParseSet(discSet))
                {
                    b |= this.Discs.TryRemove(disc.ToString(), out Disc d);
                }
            }
            if (b)
                this.discChangerService.needsSaving = true;
            await hubContext.Clients.All.SendAsync("Reload");
        }

        internal async Task ShiftDiscs(string sourceSlotsSetString, string destinationSlotsSetString)
        {
            var sourceSlotsSet = ParseSet(sourceSlotsSetString);
            var destinationSlotsSet = ParseSet(destinationSlotsSetString);
            var sourceSlotsSetCount = sourceSlotsSet.Count();
            var destinationSlotsSetCount = destinationSlotsSet.Count();
            if (sourceSlotsSetCount != destinationSlotsSetCount)
                throw new Exception($"{sourceSlotsSetString} count ({sourceSlotsSetCount})!={destinationSlotsSetString} count ({destinationSlotsSetCount})");
            if (ScanCancellationTokenSource != null)
                throw new Exception("There is a scan in progress on " + this.Key);
            lock (this.Discs)
            {
                var movedDiscs = sourceSlotsSet.Zip(destinationSlotsSet, (src, dst) =>
                {
                    this.Discs.TryRemove(src.ToString(), out Disc d);
                    var newSlot = dst.ToString();
                    d.Slot = newSlot;
                    return KeyValuePair.Create(newSlot, d);
                }).ToArray();//force it to execute
                foreach (var movedDisc in movedDiscs)
                {
                    if (!this.Discs.TryAdd(movedDisc.Key, movedDisc.Value))
                    {
                        System.Diagnostics.Debug.WriteLine($"Changer {Name} trouble reinserting moved disc in slot {movedDisc.Key}");
                    }
                }
            }
            this.discChangerService.needsSaving = true;
            await hubContext.Clients.All.SendAsync("Reload");
        }

        internal string GetPopulatedSlots(string slotsSet)
        {
            return toSetString(ParseSet(slotsSet).Where(i => Discs.ContainsKey(i.ToString())));
        }

        internal string GetDiscsShiftDestination(string populatedSlotsSetString, int offset)
        {
            var src = ParseSet(populatedSlotsSetString);
            var dst = src.Select(i => i + offset);
            var e = dst.Except(src);
            bool invalid = e.Any(i => { var s = i.ToString(); return !ValidSlot(s) || Discs.ContainsKey(s); });
            return !invalid ? toSetString(dst) : null;
        }

        protected abstract bool ValidSlot(string s);

        public static string toSetString(BitArray discExistBitArray)
        {
            StringBuilder sb = new StringBuilder();
            int l = discExistBitArray.Length;
            bool v = false;
            int start = 0;
            for (int i = 0; i <= l; i++)
            {
                bool b = i < l && discExistBitArray.Get(i);
                if (b == v)
                    continue;
                if (b && !v)
                    start = i + 1;
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
        public static string toSetString(IEnumerable<int> list) //requires ascending order
        {
            StringBuilder sb = new StringBuilder();
            int? start = null;
            int? end = null;
            foreach (int i in list)
            {
                if (start is null)
                {
                    start = i; end = i;
                }
                else
                {
                    if (end + 1 == i)
                        end = i;
                    else
                    {
                        if (sb.Length > 0)
                            sb.Append(',');
                        sb.Append(start);
                        if (end > start)
                        {
                            sb.Append('-');
                            sb.Append(end);
                        }
                        start = i; end = i;
                    }
                }
            }
            if (start is not null)
            {
                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(start);
                if (end > start)
                {
                    sb.Append('-');
                    sb.Append(end);
                }
            }
            return sb.ToString();
        }

        public abstract Task<string> Control(string command);

        public virtual bool SupportsCommand(string command)
        {
            return false;
        }
        internal abstract Task<string> Test();

        static public readonly System.Text.Encoding iso_8859_1 = System.Text.Encoding.GetEncoding("iso-8859-1");
        internal virtual void Disconnect()
        {
            if (this.serialPort != null && this.serialPort.IsOpen) { serialPort.Close(); }; serialPort?.Dispose(); serialPort = null;
            if (this.networkSocket != null) { this.networkSocket.Close(); this.networkSocket.Dispose(); this.networkSocket = null; }
        }

        public ConcurrentDictionary<string, Disc> Discs = new ConcurrentDictionary<string, Disc>();
        public bool? HardwareFlowControl { get; set; }

        protected DiscChangerService discChangerService;


        public class Status
        {
            static TimeSpan StatusTimeSpan = TimeSpan.FromMinutes(2);
            private DateTime timeStamp = DateTime.Now;
            public bool IsOutDated() { return DateTime.Now - timeStamp > StatusTimeSpan; }
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
        protected Status currentStatus = null;
        public virtual Status CurrentStatus()
        {
            return currentStatus;
        }
        public virtual void ClearStatus()
        {
            currentStatus = null;
        }

        public static string GetBytesToString(byte[] value)
        {
            return value != null ? BitConverter.ToString(value) : "null";
        }

        protected BufferBlock<string> bufferBlockDiscSlot;
        protected bool disposedValue;



        public abstract Task Connect();
        public abstract Task Connect(DiscChangerService discChangerService, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger);
        public virtual void InitiateStatusUpdate() { }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    ScanCancellationTokenSource?.Dispose(); ScanCancellationTokenSource = null;
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
    public abstract class DiscChangerSony : DiscChanger
    {
        public enum Command : byte
        {
            DISC_DIRECT_SET = 0x4A,
            POWER_SET = 0x60,
            BROADCAST_MODE_SET = 0x62,
            CIS_COMMAND_VERSION = 0x80,
            STATUS_DATA = 0x82,
            DISC_EXIST_BIT = 0x8C,
            DISC_DATA = 0x8A,
            TOC_DATA = 0x8B,
            MODEL_NAME = 0xA0
        };
        public const int SlotCount = 400;
        public const int BitsPerByte = 8;
        public const int SlotBytes = SlotCount / BitsPerByte;
        protected override bool ValidSlot(string slotString) //allow non-numeric slots, perhaps rental slot, for future enhancements
        {
            return Int32.TryParse(slotString, out int slot) && slot >= 1 && slot <= SlotCount;
        }

        public bool AdjustLastTrackLength { get; set; } = true;
        public bool ReverseDiscExistBytes { get; set; } = false;

        //BlockingCollection<byte[]> responses = new BlockingCollection<byte[]>();
        BufferBlock<byte[]> responses = new BufferBlock<byte[]>();

        protected byte PDC = 0xD0;
        protected byte ResponsePDC = 0xD8;
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
        protected virtual void retrieveNonBroadcastData() { }
        protected static TimeSpan CommandTimeOut = TimeSpan.FromSeconds(1);
        protected static TimeSpan LongCommandTimeOut = TimeSpan.FromSeconds(10);
        internal override BitArray getDiscsPresent()
        {
            byte cmd;
            byte[] discExistBitReq = null;
            if (this.networkSocket != null)
            {
                cmd = (byte)DiscChangerSonyBD.Command.DISC_EXIST_BIT_NETWORK;
                discExistBitReq = new byte[] { PDC, cmd, 0 };
            }
            else
            {
                cmd = (byte)Command.DISC_EXIST_BIT;
                SendCommand(new byte[] { PDC, cmd });
            }
            List<byte> discExistBit = new List<byte>(SlotBytes);
            byte[] b; byte count = 0;
            do
            {
                if (discExistBitReq != null)
                {
                    discExistBitReq[2] = count;
                    SendCommand(discExistBitReq);
                }
                b = responses.Receive(LongCommandTimeOut);
                var l = b.Length;
                if (l > 2 && b[0] == ResponsePDC && b[1] == cmd)
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
                    throw new Exception($"DISC_EXIST_BIT {count} error: {GetBytesToString(b)}");
            }
            while (count < 5);

            System.Diagnostics.Debug.WriteLine("Received " + count + " packets from " + GetBytesToString(discExistBitReq));
            return new BitArray(discExistBit.ToArray());
        }
        internal override string getDiscsToScan()
        {
            BitArray discExistBitArray = getDiscsPresent();
            foreach (var key in this.Discs.Keys)
            {
                if (Int32.TryParse(key, out int position))
                {
                    discExistBitArray.Set(position - 1, false);
                }
            }
            return toSetString(discExistBitArray);
        }


        internal override string getDiscsToDelete()
        {
            BitArray discExistBitArray = getDiscsPresent();
            var l = discExistBitArray.Length;
            BitArray discToDeleteBitArray = new BitArray(l);
            foreach (var key in this.Discs.Keys)
            {
                if (Int32.TryParse(key, out int position) && position <= l && !discExistBitArray.Get(position - 1))
                {
                    discToDeleteBitArray.Set(position - 1, true);
                }
            }
            return toSetString(discToDeleteBitArray);
        }

        public override async Task<bool> LoadDisc(int disc)
        {
            return await DiscDirect(disc, null, null, 0x01);
        }

        internal override async Task<string> Test()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Power OK: " + await Control("power_on"));
            byte[] b = await ProcessPacketCommandAsync(new byte[] { PDC, (byte)Command.MODEL_NAME });
            var i = Array.IndexOf(b, (byte)0, 2);
            var count = i != -1 ? i - 2 : b.Length - 2;
            var modelName = System.Text.Encoding.ASCII.GetString(b, 2, count);
            sb.Append("Model name="); sb.AppendLine(modelName);
            b = await ProcessPacketCommandAsync(new byte[] { PDC, (byte)Command.CIS_COMMAND_VERSION });
            sb.AppendLine($"System Control Version={b[2]}.{b[3]}, Interface Control Version={b[4]}.{b[5]}");
            sb.AppendLine(currentStatus?.ToString() ?? "Unknown status");
            return sb.ToString();
        }

        public static byte CheckSum(byte[] a)
        {
            int s = 0;
            foreach (byte b in a)
            {
                s = (s + b) & 0xFF;
            }
            return (byte)s;
        }
        public static int? FromBCD(byte h, byte l)
        {
            if (l == 0xFF || h == 0xFF)
                return null;
            return (h >> 4) * 1000 + (h & 0xF) * 100 + (l >> 4) * 10 + (l & 0xF);
        }
        public static void ToBCD(int? i, out byte h, out byte l)
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
        public static int? FromBCD(byte b)
        {
            if (b == 0xFF)
                return null;
            return (b >> 4) * 10 + (b & 0xF);
        }
        internal void SendCommand(byte[] command, bool discardInBuffer = true)
        {
            lock (CurrentConnection())
            {
                if (discardInBuffer)
                {
                    DiscardInBuffer();
                    responses.TryReceiveAll(out IList<byte[]> _);
                }
                int l = command.Length;
                byte[] header = new byte[2] { 2, (byte)l };
                byte[] trailer = new byte[1] { (byte)((-l - CheckSum(command)) & 0xFF) };
                byte[] combined = header.Concat(command).Concat(trailer).ToArray();
                WriteBytes(combined, 0, combined.Length);
            }
        }

        internal byte[] ReadPacketData()
        {
            int l = ReadByte();
            byte[] buffer = new byte[l];
            int i = 0;
            while (i < l)
            {
                int n = ReadBytes(buffer, i, l - i);
                i += n;
            }
            int checkSum = (l + ReadByte() + DiscChangerSony.CheckSum(buffer)) & 0xFF;
            if (checkSum != 0)
                throw new Exception($"Checksum error {checkSum} of  packet {GetBytesToString(buffer)}");
            System.Diagnostics.Debug.WriteLine("Packet Received: " + GetBytesToString(buffer));
            return buffer;
        }
        internal byte[] ReadByteOrPacket()
        {
            int b = ReadByte();
            if (b == 2)
                return ReadPacketData();
            System.Diagnostics.Debug.WriteLine("Byte Received: " + b.ToString("X"));
            return new byte[1] { (byte)b };
        }
        internal byte[] ReadNextPacketOfType(byte cmd)
        {
            bool found;
            byte[] b;
            do
            {
                b = ReadByteOrPacket();
                if (b == null) throw new Exception("Timeout while retrieving disc information, abandoning");
                if (b[0] == ResponsePDC && b.Length >= 2 && b[1] == 0x0E)
                    return null; // BD player recognized the request but cannot execute it
                found = b[0] == ResponsePDC && b.Length >= 2 && b[1] == cmd;
                if (!found && !processPacket(b))
                    responses.Post(b);
            }
            while (!found);
            return b;
        }

        internal byte[] ReadPacket()
        {
            for (; ; )
            {
                byte[] b = ReadByteOrPacket();
                if (b.Length > 1)
                    return b;
                responses.Post(b);
            }
        }


        private async Task<byte[]> ProcessPacketCommandAsync(byte[] command)
        {
            SendCommand(command);
            byte[] b; int count = 0;
            do
            {
                try
                {
                    b = await responses.ReceiveAsync(CommandTimeOut);
                }
                catch (TimeoutException) { b = null; }
                count++;
            }
            while (count < 10 && (b == null || b.Length < 2 || b[0] != this.ResponsePDC || b[1] != command[1]));
            if (count == 10)
            {
                string err = "Did not get response to packet command: " + GetBytesToString(command);
                System.Diagnostics.Debug.WriteLine(err);
                throw new Exception(err);
            }
            return b;
        }

        private async Task<bool> ProcessAckCommandAsync(byte[] command)
        {
            SendCommand(command);
            byte[] b; int count = 0;
            do
            {
                try
                {
                    b = await responses.ReceiveAsync(CommandTimeOut);
                }
                catch (TimeoutException) { b = null; }
                count++;
            }
            while (count < 10 && (b == null || b.Length != 1));
            if (count == 10)
            {
                string err = "Did not get response to ack command: " + GetBytesToString(command);
                System.Diagnostics.Debug.WriteLine(err);
                throw new Exception(err);
            }
            if (b[0] == ACK)
                return true;
            else if (b[0] == NACK)
                return false;
            else
            {
                string err = "Received 1-byte, neither ACK nor NACK, response from " + GetBytesToString(command) + ": " + GetBytesToString(b);
                System.Diagnostics.Debug.WriteLine(err);
                throw new Exception(err);
            }
        }
        private async Task<bool> ProcessAckCommand(byte command)
        {
            return await ProcessAckCommandAsync(new byte[] { PDC, command });
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

        private void NetworkReceiveCallback(IAsyncResult ar)
        {
            try
            {
                Socket socket = (Socket)ar.AsyncState;
                int bytesRead = socket.EndReceive(ar);
                if (bytesRead != 1)
                    System.Diagnostics.Debug.WriteLine($"unexpected EndReceive result: {bytesRead}");
                else
                {
                    do
                    {
                        byte startByte = networkBuffer[0];
                        if (startByte != 2)
                            responses.Post(new byte[1] { startByte });
                        else if (networkSocket != null && networkSocket.Connected)
                        {
                            byte[] b = ReadPacketData();
                            HandlePacket(b);
                        }
                    } while (networkSocket != null && networkSocket.Connected && networkSocket.Available >= 1 && networkSocket.Receive(networkBuffer, 0, 1, SocketFlags.None) == 1);
                    if (networkSocket != null && networkSocket.Connected)
                        networkSocket.BeginReceive(networkBuffer, 0, NetworkBufferSize, 0, new AsyncCallback(NetworkReceiveCallback), networkSocket);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"NetworkReceiveCallback exception {e.Message} in disc changer {Name}");
            }
        }

        private void SerialPortReceiveCallback(
                            object sender,
                            SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            lock (sp)
            {
                int btr;
                while ((btr = sp.BytesToRead) > 0)
                {
                    System.Diagnostics.Debug.Write($"about to read {btr}");
                    byte[] b = ReadByteOrPacket();
                    if (b.Length == 1)
                    {
                        responses.Post(b); continue;
                    }
                    System.Diagnostics.Debug.WriteLine($"{btr}->{sp.BytesToRead} Packet Received.");
                    HandlePacket(b);
                }
            }
        }
        private void HandlePacket(byte[] b)
        {
            try
            {
                if (b[0] == ResponsePDC)
                {
                    if (!processPacket(b))
                        responses.Post(b);
                    else if (newDisc != null && newDisc.hasBroadcastData())
                    {
                        b = null;
                        try
                        {
                            for (; ; )
                            {
                                b = ReadPacket();
                                if (b.Length < 4 || b[0] != ResponsePDC || FromBCD(b[2], b[3])?.ToString() != newDisc.Slot)
                                {
                                    newDisc = null;
                                    break;
                                }
                                if (!processPacket(b))
                                    responses.Post(b);
                                b = null;
                            }
                        }
                        catch (TimeoutException)
                        {
                            System.Diagnostics.Debug.WriteLine("No serial port packets forthcoming for new disc slot: " + newDisc.Slot);
                        }
                        catch (SocketException e)
                        {
                            if (e.SocketErrorCode != SocketError.TimedOut)
                                throw;
                            System.Diagnostics.Debug.WriteLine("No network packets forthcoming for new disc slot: " + newDisc.Slot);
                        }
                        if (newDisc != null)
                        {
                            this.retrieveNonBroadcastData();
                            newDisc.DiscChanger = this;
                            if (bufferBlockDiscSlot != null)
                                bufferBlockDiscSlot.Post(newDisc.Slot);

                            if (discChangerService != null &&
                                (newDisc as DiscSony)?.DiscData?.DiscType != Disc.DiscTypeNone &&
                                (!Discs.TryGetValue(newDisc.Slot, out Disc oldDisc) || newDisc != oldDisc))
                                discChangerService.AddDiscData(newDisc);
                            newDisc = null;
                        }
                        if (b != null)
                        {
                            if (b[0] != ResponsePDC)
                                throw new Exception($"Unrecognized ResponsePDC {b[0]}");
                            if (!processPacket(b))
                                responses.Post(b);
                        }
                    }
                }
                else
                    throw new Exception($"Unrecognized ResponsePDC {b[0]}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception {ex.ToString()} on packet: {GetBytesToString(b)}");
            }
        }
        protected DiscSony.TOC receiveTOC(int discNumber, byte cmd, byte[] initialPacket)
        {
            List<string> Titles = new List<string>();
            ConcurrentDictionary<string, List<int>> TitleFrames = new ConcurrentDictionary<string, List<int>>();
            byte mode;
            int expectedIPPacketNum = 1;
            byte[] b;
            do
            {
                if (initialPacket != null)
                {
                    b = initialPacket; initialPacket = null;
                }
                else
                {
                    if (cmd == (byte)DiscChangerSonyBD.Command.TOC_DATA_NETWORK)
                    {
                        byte[] ipTOCDataReq = new byte[] { PDC, cmd, 0, 0, 1, (byte)(expectedIPPacketNum >> 8), (byte)(expectedIPPacketNum & 0xFF) };
                        SendCommand(ipTOCDataReq);
                    }
                    b = ReadNextPacketOfType(cmd);
                }

                if (b[0] != ResponsePDC || b[1] != cmd)
                    throw new Exception($"Unexpected TOC packet {b[0]} {b[1]}");
                int? packetDiscNumber = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
                if (packetDiscNumber != discNumber)
                    throw new Exception($"Unexpected TOC packet disc number {packetDiscNumber} vs. expected {discNumber}");
                byte titleNumber = b[4];
                int offset = 0;
                if (cmd == (byte)DiscChangerSonyBD.Command.TOC_DATA_NETWORK)
                {
                    int dataPacketNum = ((b[5] << 8) | b[6]);
                    if (titleNumber != 1)//Data Type "0x00 : Information of IP_TOC_DATA 0x01 : Data of IP_TOC_DATA"							
                        throw new Exception($"Unexpected TOC data type {titleNumber}");
                    offset = 3;
                    titleNumber = b[4 + offset];
                    if (expectedIPPacketNum != dataPacketNum)
                        throw new Exception($"Unexpected IP_TOC_DATA packet disc number {dataPacketNum} vs. expected {expectedIPPacketNum}");
                    expectedIPPacketNum++;
                }
                int l = b.Length;
                mode = b[l - 1];
                if (titleNumber != 0xFF)
                {
                    string titleNumberStr = titleNumber != 0xCD ? FromBCD(titleNumber).Value.ToString() : "CD";
                    if (!TitleFrames.TryGetValue(titleNumberStr, out List<int> frames))
                    {
                        Titles.Add(titleNumberStr);
                        frames = new List<int>();
                        TitleFrames[titleNumberStr] = frames;
                    }
                    int i;
                    for (i = 5 + offset; i + 3 < l; i += 4)
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
                        break;
                    case 0xEE:
                        System.Diagnostics.Debug.WriteLine("TOC data end of title");
                        break;
                    case 0xFF:
                        System.Diagnostics.Debug.WriteLine("TOC data end of disc");
                        break;
                    default:
                        throw new Exception("Unknown mode: " + mode.ToString("X"));
                }
            } while (mode != 0xff);

            DiscSony.TOC toc = new DiscSony.TOC();
            toc.Titles = Titles.ToArray();
            toc.TitleFrames = new ConcurrentDictionary<string, int[]>(
                TitleFrames.Select(kvp => new KeyValuePair<string, int[]>(kvp.Key, kvp.Value.ToArray())));
            return toc;
        }
        internal virtual bool processPacket(byte[] b)
        {
            DiscChangerSony.Command cmd = (DiscChangerSony.Command)b[1];
            int? discNumber = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
            string discNumberString = discNumber?.ToString();
            switch (cmd)
            {
                case DiscChangerSony.Command.STATUS_DATA:
                    Status newStatus = new Status();
                    newStatus.discNumber = discNumber;
                    newStatus.titleAlbumNumber = FromBCD(b[4], b[5]);
                    newStatus.chapterTrackNumber = FromBCD(b[6], b[7]);
                    newStatus.status = b[8];
                    newStatus.statusString = statusCode2String[newStatus.status];
                    newStatus.mode = b[9];
                    newStatus.modeDisc = (newStatus.mode & 32) != 0 ? "all" : "one";
                    newStatus.modeBroadCast = (byte)((newStatus.mode >> 2) & 3);
                    newStatus.setupLock = (byte)(newStatus.mode & 1);
                    currentStatus = newStatus;
                    string msg5 = "Status: " + currentStatus.ToString();
                    System.Diagnostics.Debug.WriteLine(msg5);
                    _ = hubContext?.Clients.All.SendAsync("StatusData",
                                                       Key,
                                                       currentStatus.discNumber,
                                                       currentStatus.titleAlbumNumber,
                                                       currentStatus.chapterTrackNumber,
                                                       currentStatus.statusString,
                                                       currentStatus.modeDisc);
                    break;
                case DiscChangerSony.Command.DISC_DATA:
                    DiscSony.Data dd = new DiscSony.Data();
                    byte discTypeByte = b[4];
                    dd.DiscType = DiscSony.discType2String[discTypeByte];
                    dd.StartTrackTitleAlbum = FromBCD(b[5], b[6]);
                    dd.LastTrackTitleAlbum = FromBCD(b[7], b[8]);
                    dd.HasMemo = (b[9] & 1) != 0;
                    dd.HasText = (b[9] & 16) != 0;
                    string msg4 = "DiscData: " + (discNumber ?? -1) + "/" + (dd.StartTrackTitleAlbum ?? -1) + "/" + (dd.LastTrackTitleAlbum ?? -1) + ":" + dd.DiscType + "," + Convert.ToString(b[9], 2);
                    System.Diagnostics.Debug.WriteLine(msg4);
                    if (discNumber.HasValue && discTypeByte != 0xFF)
                    {
                        if (newDisc == null || newDisc.Slot != discNumberString || (newDisc as DiscSony)?.DiscData != null)
                            newDisc = createDisc(discNumberString);
                        ((DiscSony)newDisc).DiscData = dd;
                    }
                    break;
                case DiscChangerSony.Command.TOC_DATA:
                    if (discNumber.HasValue)
                    {
                        DiscSony.TOC toc = receiveTOC(discNumber.Value, (byte)cmd, b);

                        if (newDisc == null || newDisc.Slot != discNumberString || (newDisc as DiscSony)?.TableOfContents != null)
                            newDisc = createDisc(discNumberString);
                        ((DiscSony)newDisc).TableOfContents = toc;
                    }
                    break;
                default:
                    return false;
                    //                                        System.Diagnostics.Debug.WriteLine("Unrecognized:" + cmd.ToString("X"));
            }
            return true;
        }
        public const int ReadTimeout = 6000;//in ms
        public const int WriteTimeout = 10000;//in ms

        private void OpenConnection()
        {
            switch (this.Connection)
            {
                case CONNECTION_SERIAL_PORT:
                    System.Diagnostics.Debug.WriteLine("Opening serial port: " + PortName);
                    try
                    {
                        serialPort = new SerialPort(PortName);

                        serialPort.BaudRate = 9600;
                        serialPort.Parity = Parity.None;
                        serialPort.StopBits = StopBits.One;
                        serialPort.DataBits = 8;
                        serialPort.Handshake = HardwareFlowControl == true ? Handshake.RequestToSend : Handshake.None;
                        serialPort.RtsEnable = HardwareFlowControl == true;
                        serialPort.ReadTimeout = ReadTimeout;
                        serialPort.WriteTimeout = WriteTimeout;
                        serialPort.Open();
                        serialPort.DataReceived += SerialPortReceiveCallback;
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
                    break;
                case CONNECTION_NETWORK:
                    System.Diagnostics.Debug.WriteLine($"Opening socket: {NetworkHost}{NetworkPort}");
                    try
                    {
                        networkSocket = new Socket(AddressFamily.InterNetwork,
                                                   SocketType.Stream,
                                                   ProtocolType.Tcp);
                        networkSocket.ReceiveTimeout = ReadTimeout;
                        networkSocket.SendTimeout = WriteTimeout;
                        networkSocket.Connect(NetworkHost, NetworkPort.Value);
                        networkBuffer = new byte[NetworkBufferSize];
                        networkSocket.BeginReceive(networkBuffer, 0, NetworkBufferSize, 0, new AsyncCallback(NetworkReceiveCallback), networkSocket);
                    }
                    catch
                    {
                        try
                        {
                            networkSocket?.Close(); networkSocket?.Dispose();
                        }
                        catch { };
                        networkSocket = null;
                        networkBuffer = null;
                        throw;
                    }
                    break;
            }
        }


        public override async Task Connect()
        {
            try
            {
                ClearStatus();
                OpenConnection();
                await ProcessAckCommandAsync(new byte[] { PDC, (byte)Command.BROADCAST_MODE_SET, 0x02 });
                InitiateStatusUpdate();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception in Connect(): " + e.Message);
            }
        }
        public override void InitiateStatusUpdate()
        {
            SendCommand(new byte[] { PDC, (byte)Command.STATUS_DATA }, false);
        }
        static protected TimeSpan PowerTimeSpan = TimeSpan.FromMilliseconds(200);
        public override async Task<string> Control(string command)
        {
            if (command.StartsWith("power"))
            {
                if (CurrentConnection() == null)
                    OpenConnection();
                byte desiredState = command == "power_on" || (command == "power" && (currentStatus == null || currentStatus.status == 0)) ? (byte)1 : (byte)0;
                var powerCommand = new byte[] { PDC, (byte)Command.POWER_SET, desiredState };
                int iterations = desiredState * 9 + 1;
                int i;
                bool success = false;
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                string r = null;

                for (i = 0; !success && i < iterations; i++)
                {
                    SendCommand(powerCommand, i == 0);
                    sw.Start();
                    try
                    {
                        byte[] response = await responses.ReceiveAsync(PowerTimeSpan);
                        if (response.Length > 1 && responses.TryReceiveAll(out IList<byte[]> l))
                        {
                            System.Diagnostics.Debug.WriteLine($"{command} attempt: {i}, skipping large response packet & got {l.Count} packets");
                            response = l.First(p => p.Length == 1);
                        }
                        if (response.Length == 1)
                        {
                            success = true; r = ack2String[response[0]];
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (TimeoutException) { }
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
                            ack = await ProcessAckCommandAsync(new byte[] { PDC, (byte)Command.BROADCAST_MODE_SET, 0x02 });
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
                bool b = await ProcessAckCommand(commandCode);
                return b ? "ACK" : "NACK";
            }
        }
        protected static readonly TimeSpan NoExecTimeOut = TimeSpan.FromSeconds(3);
        public async Task<bool> DiscDirect(int? discNumber, int? titleAlbumNumber, int? chapterTrackNumber, byte control = 0x00)
        {
            ToBCD(discNumber ?? 0, out byte discNumberH, out byte discNumberL);
            byte trackTitleNumberH, trackTitleNumberL;
            byte chapterNumberH, chapterNumberL;
            if (titleAlbumNumber != null)
            {
                ToBCD(titleAlbumNumber ?? 0, out trackTitleNumberH, out trackTitleNumberL);
                ToBCD(chapterTrackNumber ?? 0, out chapterNumberH, out chapterNumberL);
            }
            else
            {
                ToBCD(chapterTrackNumber ?? 0, out trackTitleNumberH, out trackTitleNumberL);
                chapterNumberH = 0; chapterNumberL = 0;
            }
            //            byte control = pause? 0x01:0x00;//Play, 0x01 for pause
            var discDirectSetCommand = new byte[] { PDC, (byte)Command.DISC_DIRECT_SET, discNumberH, discNumberL, trackTitleNumberH, trackTitleNumberL, chapterNumberH, chapterNumberL, control };
            bool result = await ProcessAckCommandAsync(discDirectSetCommand);
            if (result)
            {
                try
                {
                    byte[] b = await responses.ReceiveAsync(NoExecTimeOut);
                    if (b.Length >= 2 && b[0] == ResponsePDC && b[1] == 0x0E)
                        result = false;
                }
                catch (TimeoutException) { }
            }
            return result;
        }
    }

    public class DiscChangerSonyDVD : DiscChangerSony
    {
        public const string DVP_CX777ES = "Sony DVP-CX777ES";
        public new enum Command : byte
        {
            TEXT_DATA = 0x90
        };

        public DiscChangerSonyDVD(string type)
        {
            this.Type = type;
            this.ReverseDiscExistBytes = false;
            this.AdjustLastTrackLength = true;//This appears to be necessary for both CX777ES and CX7000ES.
        }
        public override bool SupportsCommand(string command)
        {
            return command != "open";
        }
        public override async Task Connect(DiscChangerService discChangerService, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger)
        {
            this.logger = logger;
            this.hubContext = hubContext;
            this.discChangerService = discChangerService;
            PDC = (byte)0xD0;
            ResponsePDC = (byte)0xD8;
            await Connect();
        }

        internal override Type getDiscType()
        {
            return typeof(DiscSonyDVD);
        }
        internal override Disc createDisc(string slot = null)
        {
            return new DiscSonyDVD(slot);
        }

        internal override bool processPacket(byte[] b)
        {
            Command cmd = (Command)b[1];
            int? discNumber = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
            string discNumberString = discNumber?.ToString();
            switch (cmd)
            {
                case Command.TEXT_DATA:
                    int? titleTrackNumber = FromBCD(b[4]); //reserved 0x00
                    byte type = b[5]; //reserved 0x00
                    string characterSet = DiscSonyDVD.characterCodeSet2String[b[8]];
                    byte expectedPacketNumber = 0;
                    List<byte> text = new List<byte>();
                    bool error = false;
                    do
                    {
                        byte packetNumber = b[9];
                        if (packetNumber != expectedPacketNumber || b.Length < 4 || FromBCD(b[2], b[3]) != discNumber)
                        {
                            error = true; break;
                        }
                        if (b.Last() == (byte)0x00)
                        {
                            int i = b.Length - 2;
                            while (i >= 10 && b[i] == (byte)0x00)
                                i--;
                            text.AddRange(b.Skip(10).Take(i - 9));
                            break;
                        }
                        text.AddRange(b.Skip(10));
                        expectedPacketNumber++; b = ReadPacket();
                    }
                    while (!error);
                    if (error)
                        throw new Exception("Error reading TEXT_DATA packets");
                    DiscSonyDVD.Text td = new DiscSonyDVD.Text();
                    string msg = "TEXT_DATA: " + discNumber;

                    var textArray = text.ToArray();
                    if (textArray.Length > 0)
                    {
                        td.Language = DiscSonyDVD.languageCode2String[(ushort)((b[6] << 8) | b[7])];
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
                        if (newDisc != null &&
                            newDisc.Slot == discNumberString &&
                            newDisc is DiscSonyDVD newDiscDVD &&
                            newDiscDVD.DiscText == null)
                        {
                            newDiscDVD.DiscText = td;
                            var dd = newDiscDVD.DiscData;
                            if (dd != null && dd.DiscType.StartsWith("SACD") &&
                                Discs.TryGetValue(discNumberString, out Disc existingDisc) &&
                                existingDisc is DiscSonyDVD existingDiscDVD &&
                                existingDiscDVD.DiscData?.DiscType != null &&
                                existingDiscDVD.DiscData.DiscType.StartsWith("SACD") &&
                                newDiscDVD.TableOfContents == null &&
                                existingDiscDVD.TableOfContents?.TitleFrames != null &&
                                existingDiscDVD.TableOfContents.TitleFrames.ContainsKey("CD") &&
                                dd.TrackCount() >= existingDiscDVD.TableOfContents.TitleFrames["CD"].Length &&
                                (String.IsNullOrEmpty(existingDiscDVD.DiscText.TextString) ||
                                 String.Compare(existingDiscDVD.DiscText.TextString, newDiscDVD.DiscText.TextString, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0))
                            {
                                newDiscDVD.DateTimeAdded = existingDiscDVD.DateTimeAdded; //preserve datetime from existing
                                var toc = existingDiscDVD.TableOfContents;
                                if (toc.Source == null)
                                {
                                    toc = toc.ShallowCopy();
                                    toc.Source = existingDiscDVD.DiscData.DiscType;
                                }
                                newDiscDVD.TableOfContents = toc;
                            }
                        }
                        else
                        {
                            throw new Exception($"Got unexpected TEXT_DATA: {td.Language}/{td.TextString}");
                        }
                    }
                    return true;
                default:
                    return base.processPacket(b);
            }
        }
    }
    public class DiscChangerSonyBD : DiscChangerSony
    {
        public const string BDP_CX7000ES = "Sony BDP-CX7000ES";
        public new enum Command : byte
        {
            DISC_INFORMATION = 0x8D,
            ID_DATA_SERIAL = 0x8E,
            TOC_DATA_NETWORK = 0xCB,
            DISC_EXIST_BIT_NETWORK = 0xCC,
            ID_DATA_NETWORK = 0xCE
        };

        public static readonly string[] CommandModes = new string[] { "BD1", "BD2", "BD3" };
        public static readonly Dictionary<string, byte> CommandMode2PDC = new Dictionary<string, byte>(Enumerable.Zip(CommandModes, Enumerable.Range(0x80, CommandModes.Length), (m, pdc) => new KeyValuePair<string, byte>(m, (byte)pdc)));
        public static readonly Dictionary<string, byte> CommandMode2ResponsePDC = new Dictionary<string, byte>(Enumerable.Zip(CommandModes, Enumerable.Range(0x88, CommandModes.Length), (m, pdc) => new KeyValuePair<string, byte>(m, (byte)pdc)));

        public DiscChangerSonyBD(string type)
        {
            this.Type = type;
            this.ReverseDiscExistBytes = true;//Apparent difference between CX777ES and CX7000ES
            this.AdjustLastTrackLength = true;//This appears to be necessary for both CX777ES and CX7000ES.
        }
        public override bool SupportsCommand(string command)
        {
            return true;
        }
        public override async Task Connect(DiscChangerService discChangerService, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger)
        {
            this.logger = logger;
            this.hubContext = hubContext;
            this.discChangerService = discChangerService;
            PDC = CommandMode2PDC[CommandMode];
            ResponsePDC = CommandMode2ResponsePDC[CommandMode];
            await Connect();
        }

        internal override Type getDiscType()
        {
            return typeof(DiscSonyBD);
        }
        internal override Disc createDisc(string slot = null)
        {
            return new DiscSonyBD(slot);
        }

        byte[] ReadIDResponse(int desiredDiscNumber, byte desiredDataType, byte cmd, int firstPacketNumber, byte[] initialPacket = null)
        {
            int? discNumber;
            byte dataType;
            List<byte> data = null;
            byte[] b;
            int l;
            int packetCounter = firstPacketNumber;
            do
            {
                if (initialPacket != null)
                {
                    b = initialPacket; initialPacket = null;
                }
                else
                {
                    if (cmd == (byte)DiscChangerSonyBD.Command.ID_DATA_NETWORK)
                        SendCommand(new byte[] { PDC, cmd, 0, 0, desiredDataType, (byte)(packetCounter >> 8), (byte)(packetCounter & 0xFF) });
                    b = ReadNextPacketOfType(cmd);
                }

                if (b == null)
                    return null;
                System.Diagnostics.Debug.WriteLine($"Received raw data {GetBytesToString(b)}");
                l = b.Length;
                discNumber = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
                if (!discNumber.HasValue || discNumber.Value != desiredDiscNumber)
                    throw new Exception($"Received Disc ID response disc number {discNumber} instead of {desiredDiscNumber}");
                dataType = b[4];
                if (dataType != desiredDataType)
                    throw new Exception($"Received Disc ID response data type {dataType} instead of {desiredDataType}");
                int length = (b[5] << 24) | (b[6] << 16) | (b[7] << 8) | b[8];//FromBCD(b[5], b[6]);
                int packetNumber = (b[9] << 8) | b[10];//FromBCD(b[5], b[6]);
                System.Diagnostics.Debug.WriteLine($"Received raw data {discNumber} {dataType} {packetNumber} {length}");
                if (packetCounter != packetNumber)
                    throw new Exception($"ID Data packet out of sequence {packetNumber} instead of {packetCounter}");
                if (data == null)
                    data = new List<byte>(length);
                const int metaDataOffset = 11;
                data.AddRange(b.Skip(metaDataOffset).Take(l - (metaDataOffset + 1))); packetCounter++;
            } while (b[l - 1] == 0xcc);
            var dataArray = data.ToArray();
            System.Diagnostics.Debug.WriteLine($"Received ID response {discNumber} {dataType} {GetBytesToString(dataArray)}");
            return dataArray;
        }
        int ParseNetworkDiscIDPacketCount(byte[] b, out int discNumber, out DiscSonyBD.IDData.DataTypeNetwork dataType)
        {
            System.Diagnostics.Debug.WriteLine($"Received raw data {GetBytesToString(b)}");
            int l = b.Length;
            int? discNumberNullable = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
            if (!discNumberNullable.HasValue)
                throw new Exception($"Received IP Disc ID packet count response no disc number");
            discNumber = discNumberNullable.Value;
            dataType = (DiscSonyBD.IDData.DataTypeNetwork)b[4];
            int length = (b[5] << 24) | (b[6] << 16) | (b[7] << 8) | b[8];//FromBCD(b[5], b[6]);
            int packetCount = (b[9] << 8) | b[10];//FromBCD(b[5], b[6]);
            System.Diagnostics.Debug.WriteLine($"Received GetIPDiscIDPacketCount response {discNumber} {dataType} {packetCount} {length} {b[l - 1]}");
            return packetCount;
        }
        int GetIPDiscIDPacketCount(int desiredDiscNumber, DiscSonyBD.IDData.DataTypeNetwork dataType)
        {
            const byte cmd = (byte)DiscChangerSonyBD.Command.ID_DATA_NETWORK;
            SendCommand(new byte[] { PDC, cmd, 0, 0, (byte)dataType, 0, 0 });


            byte[] b = ReadNextPacketOfType(cmd);
            if (b == null)
                throw new Exception($"No matching packet response to IP Disc ID Packet count query disc {desiredDiscNumber}, data type: {dataType}");
            int packetCount = ParseNetworkDiscIDPacketCount(b, out int discNumber, out DiscSonyBD.IDData.DataTypeNetwork responseDataType);
            if (discNumber != desiredDiscNumber)
                throw new Exception($"Received IP Disc ID packet count response disc number {discNumber} instead of {desiredDiscNumber}");
            if (responseDataType != dataType)
                throw new Exception($"Received Disc ID response data type {responseDataType} instead of {dataType}");
            return packetCount;
        }


        internal override bool processPacket(byte[] b)
        {
            DiscChangerSonyBD.Command cmd = (DiscChangerSonyBD.Command)b[1];
            int? discNumber = b.Length >= 4 ? FromBCD(b[2], b[3]) : null;
            string discNumberString = discNumber?.ToString();
            DiscSonyBD newDiscBD = newDisc as DiscSonyBD;
            switch (cmd)
            {
                case DiscChangerSonyBD.Command.DISC_INFORMATION:
                    string s = parseDiscInformationPacket(b, out int? _, out string discTypeString, out int titleTrackNumber, out DiscSonyBD.Information.DataType discInfoType);
                    if (discNumber.HasValue)
                    {
                        if (newDiscBD == null) throw new Exception("Not processing new disc");
                        if (newDisc.Slot != discNumberString) throw new Exception("Slot mismatch ");
                        if (discInfoType != DiscSonyBD.Information.DataType.AlbumTitleOrDiscName) throw new Exception($"Unexpected data type {discInfoType}");
                        if (titleTrackNumber != 0) new Exception($"Unexpected track/title-specific disc info of number {titleTrackNumber}");
                        if (newDiscBD.DiscInformation?.AlbumTitleOrDiscName != null) throw new Exception("New disc already has DiscInformation");

                        DiscSonyBD.Information di = new DiscSonyBD.Information();
                        di.AlbumTitleOrDiscName = s;
                        di.DiscTypeString = discTypeString;
                        newDiscBD.DiscInformation = di;
                    }
                    return true;
                case DiscChangerSonyBD.Command.ID_DATA_SERIAL:
                    if (newDiscBD == null) throw new Exception("Not processing new disc");
                    DiscSonyBD.IDData.DataTypeSerial idDataType = (DiscSonyBD.IDData.DataTypeSerial)b[4];

                    var id = ReadIDResponse(Int32.Parse(newDiscBD.Slot), (byte)idDataType, (byte)cmd, 0, b);
                    if (id != null)
                    {
                        DiscSonyBD.IDData idData = newDiscBD.DiscIDData ?? new DiscSonyBD.IDData();
                        switch (idDataType)
                        {
                            case DiscSonyBD.IDData.DataTypeSerial.GraceNoteDiscID: idData.GraceNoteDiscID = Encoding.UTF8.GetString(id); break;
                            case DiscSonyBD.IDData.DataTypeSerial.AACSDiscID: idData.AACSDiscID = id; break;
                            default:
                                throw new Exception($"Unrecognized DiscID data type: {idDataType}");
                        }
                        newDiscBD.DiscIDData = idData;
                    }
                    return true;
                case DiscChangerSonyBD.Command.ID_DATA_NETWORK:
                    if (newDiscBD == null) throw new Exception("Not processing new disc");
                    int packetCount = ParseNetworkDiscIDPacketCount(b, out int networkDiscNumber, out DiscSonyBD.IDData.DataTypeNetwork networkDataType);
                    if (packetCount > 0 && Int32.Parse(newDiscBD.Slot) == networkDiscNumber)
                    {
                        DiscSonyBD.IDData idData = newDiscBD.DiscIDData ?? new DiscSonyBD.IDData();
                        switch (networkDataType)
                        {
                            case DiscSonyBD.IDData.DataTypeNetwork.GraceNoteDiscID_Info: idData.GraceNoteDiscIDPacketCount = packetCount; break;
                            case DiscSonyBD.IDData.DataTypeNetwork.AACSDiscID_Info: idData.AACSDiscIDPacketCount = packetCount; break;
                            default:
                                throw new Exception($"Unrecognized Network DiscID info data type: {networkDataType}");
                        }
                        newDiscBD.DiscIDData = idData;
                    }
                    return true;
                case DiscChangerSonyBD.Command.TOC_DATA_NETWORK:
                    if (b[0] != ResponsePDC)
                        throw new Exception($"Unexpected IP_TOC_DATA packet {b[0]} {b[1]}");
                    byte dataType = b[4];
                    if (dataType == 0)//Data Type "0x00 : Information of IP_TOC_DATA 0x01 : Data of IP_TOC_DATA"							
                    {
                        if (discNumber.HasValue)
                        {
                            int dataPacketCount = ((b[5] << 8) | b[6]);
                            DiscSony.TOC toc = new DiscSony.TOC();
                            toc.PacketCount = dataPacketCount;
                            if (newDisc == null || newDisc.Slot != discNumberString || (newDisc as DiscSony)?.TableOfContents != null)
                                newDisc = createDisc(discNumberString);
                            ((DiscSony)newDisc).TableOfContents = toc;
                        }
                        return true;
                    }
                    else
                        throw new Exception($"Unexpected IP_TOC_DATA packet {b[0]} {b[1]} data type: {dataType}");
                default:
                    return base.processPacket(b);
            }
        }
        public static string parseDiscInformationPacket(byte[] b, out int? discNumber, out string discTypeString, out int titleTrackNumber, out DiscSonyBD.Information.DataType dataTypeResult)
        {
            discNumber = FromBCD(b[2], b[3]);
            DiscSonyBD.Information.DiscType discType = (DiscSonyBD.Information.DiscType)b[4]; //reserved 0x00
            discTypeString = DiscSonyBD.Information.discType2String[discType]; //reserved 0x00
            titleTrackNumber = (b[5] << 8) | b[6];
            dataTypeResult = (DiscSonyBD.Information.DataType)b[7];
            byte characterCodeSet = b[8];
            const int metaDataOffset = 9;
            var i = Array.IndexOf(b, (byte)0, metaDataOffset);
            var count = i != -1 ? i - metaDataOffset : b.Length - metaDataOffset;
            return count > 0 ? Encoding.UTF8.GetString(b, metaDataOffset, count) : null;
        }
        private string retrieveDiscInformation(DiscSonyBD.Information.DataType dataType, int track)
        {
            byte trackL = (byte)(track & 0xFF);
            byte trackH = (byte)(track >> 8);
            const byte cmd = (byte)DiscChangerSonyBD.Command.DISC_INFORMATION;
            SendCommand(new byte[] { PDC, cmd, 0, 0, (byte)dataType, trackH, trackL });
            byte[] b = ReadNextPacketOfType(cmd);
            if (b == null)
                return null;
            string s = parseDiscInformationPacket(b, out int? discNumber, out string discType, out int titleTrackNumber, out DiscSonyBD.Information.DataType dataTypeResult);
            if (discNumber.ToString() != newDisc.Slot) throw new Exception($"Unexpected disc information for disc number {discNumber}");
            if (titleTrackNumber != track) throw new Exception($"Unexpected disc information for track {titleTrackNumber} instead of {track}!");
            if (dataTypeResult != dataType) throw new Exception($"Unexpected disc information data type {dataTypeResult} instead of {dataType}!");
            return s;
        }
        protected override void retrieveNonBroadcastData()
        {
            DiscSonyBD newDiscBD = newDisc as DiscSonyBD;
            int discNumber = Int32.Parse(newDiscBD.Slot);
            if (newDiscBD == null) throw new Exception("Not processing new disc");
            if (newDiscBD.TableOfContents != null &&
                newDiscBD.TableOfContents.PacketCount > 0 &&
                newDiscBD.TableOfContents.Titles == null &&
                this.networkSocket != null)
            {
                newDiscBD.TableOfContents = receiveTOC(discNumber, (byte)DiscChangerSonyBD.Command.TOC_DATA_NETWORK, null);
            }

            var di = newDiscBD.DiscInformation;
            if (di == null)
                return;//nothing to do
            if (Discs.TryGetValue(newDisc.Slot, out Disc oldDisc) &&
                oldDisc is DiscSonyBD oldDiscBD &&
                oldDiscBD.DiscInformation?.AlbumTitleOrDiscName != null &&
                oldDiscBD.DiscIDData != null &&
                oldDiscBD.DiscData == newDiscBD.DiscData &&
                di.DiscTypeString == oldDiscBD.DiscInformation.DiscTypeString &&
                di.AlbumTitleOrDiscName == oldDiscBD.DiscInformation.AlbumTitleOrDiscName)
            {
                newDiscBD.DiscInformation = oldDiscBD.DiscInformation;
                newDiscBD.DiscIDData = oldDiscBD.DiscIDData;
                return;
            }
            di.AlbumOrDiscGenre = retrieveDiscInformation(DiscSonyBD.Information.DataType.AlbumOrDiscGenre, 0);

            if (di.DiscTypeString == "CDDA")
            {
                di.TracksOrTitles = new List<DiscSonyBD.Information.TrackOrTitle>(newDiscBD.DiscData.TrackCount().Value);
                for (int track = newDiscBD.DiscData.StartTrackTitleAlbum.Value; track <= newDiscBD.DiscData.LastTrackTitleAlbum.Value; track++)
                {
                    di.TracksOrTitles.Add(new DiscSonyBD.Information.TrackOrTitle(track, retrieveDiscInformation(DiscSonyBD.Information.DataType.TrackOrTitleName, track)));
                }
            }
            var id = newDiscBD.DiscIDData ?? new DiscSonyBD.IDData();
            if (id.GraceNoteDiscID == null)
            {
                byte dataType;
                byte cmd;
                int firstPacketNumber;
                if (this.serialPort != null)
                {
                    dataType = (byte)DiscSonyBD.IDData.DataTypeSerial.GraceNoteDiscID;
                    cmd = (byte)Command.ID_DATA_SERIAL; firstPacketNumber = 0;
                    SendCommand(new byte[] { PDC, cmd, 0, 0, dataType });
                }
                else if (this.networkSocket != null)
                {
                    dataType = (byte)DiscSonyBD.IDData.DataTypeNetwork.GraceNoteDiscID;
                    cmd = (byte)Command.ID_DATA_NETWORK; firstPacketNumber = 1;

                    //int packetCount = id.GraceNoteDiscIDPacketCount;
                    //if (packetCount == 0)
                    //    packetCount = GetIPDiscIDPacketCount(discNumber, DiscSonyBD.IDData.DataTypeNetwork.GraceNoteDiscID_Info);
                    //for (int i = 1; i <= packetCount; i++)
                    //    SendCommand(new byte[] { PDC, cmd, 0, 0, dataType, (byte)(i >> 8), (byte)(i & 0xFF) });
                }
                else
                    throw new Exception("Neither Serial nor Network connection");
                byte[] b = ReadIDResponse(discNumber, dataType, cmd, firstPacketNumber);
                if (b != null)
                    id.GraceNoteDiscID = System.Text.Encoding.UTF8.GetString(b);
            }
            if (di.DiscTypeString == "BD-ROM" && id.AACSDiscID == null)
            {
                byte dataType, cmd;
                int firstPacketNumber;
                if (this.serialPort != null)
                {
                    dataType = (byte)DiscSonyBD.IDData.DataTypeSerial.AACSDiscID;
                    cmd = (byte)Command.ID_DATA_SERIAL; firstPacketNumber = 0;
                    SendCommand(new byte[] { PDC, cmd, 0, 0, dataType });
                }
                else if (this.networkSocket != null)
                {
                    dataType = (byte)DiscSonyBD.IDData.DataTypeNetwork.AACSDiscID;
                    cmd = (byte)Command.ID_DATA_NETWORK; firstPacketNumber = 1;
                    //int packetCount = id.AACSDiscIDPacketCount;
                    //if (packetCount == 0)
                    //    packetCount = GetIPDiscIDPacketCount(discNumber, DiscSonyBD.IDData.DataTypeNetwork.AACSDiscID_Info);
                    //for (int i = firstPacketNumber; i <= packetCount; i++)
                    //    SendCommand(new byte[] { PDC, cmd, 0, 0, dataType, (byte)(i >> 8), (byte)(i & 0xFF) });
                }
                else
                    throw new Exception("Neither Serial nor Network connection");
                id.AACSDiscID = ReadIDResponse(discNumber, dataType, cmd, firstPacketNumber);
            }
            newDiscBD.DiscIDData = id;
        }
    }
}
