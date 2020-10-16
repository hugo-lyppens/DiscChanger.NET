/*  Copyright 2020 Hugo Lyppens

    DiscChangerService.cs is part of DiscChanger.NET.

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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DiscChanger.Hubs;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Text.Json;

namespace DiscChanger.Models
{
    //    public class DiscChanger : IHostedService, IDisposable
    public class DiscChangerService : BackgroundService
    {
        public List<DiscChangerModel> DiscChangers { get; private set; }
        public MusicBrainz discLookup;

        public const string CONNECTION_SERIAL_PORT = "SerialPort";
        public const string CONNECTION_IP = "IP";
        public static readonly string[] ConnectionTypes = new string[] { CONNECTION_SERIAL_PORT, CONNECTION_IP };
        public const string DVP_CX777ES = "Sony DVP-CX777ES";
        public const string BDP_CX7000ES = "Sony BDP-CX7000ES";
        public static readonly string[] ChangerTypes = new string[] { String.Empty, DVP_CX777ES, BDP_CX7000ES };
        public static readonly string[] CommandModes = new string[] { "BD1", "BD2", "BD3" };

        private Dictionary<string, DiscChangerModel> key2DiscChanger;

        private readonly ILogger<DiscChangerService> _logger;
        private Timer _timer;
        private readonly IHubContext<DiscChangerHub> _hubContext;
        private string webRootPath, discChangersJsonFileName, discsPath, discsRelPath;
        BlockingCollection<Disc> discDataMessages = new BlockingCollection<Disc>();
        public void AddDiscData(Disc d ) { discDataMessages.Add(d); }
        public DiscChangerService(IWebHostEnvironment environment, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger)
        {
            _logger = logger;
            _hubContext = hubContext;
            webRootPath = environment.WebRootPath;
            discChangersJsonFileName = Path.Combine(webRootPath, "DiscChangers.json");
            discsRelPath = "Discs";
            discsPath    = Path.Combine(webRootPath, discsRelPath );
        }
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");
            System.Diagnostics.Debug.WriteLine("Timed Hosted Service running.");
            //DiscChanger dc = new DiscChanger();
            //dc.Name = "Test";
            //dc.Key = "test";
            //var d = new Disc();
            //d.TableOfContents = new Disc.TOC();
            //d.DiscText = new Disc.Text();
            //d.DiscData = new Disc.Data();

            //dc.Discs[10] = d;
            //var z = new ConcurrentDictionary<string, string>();
            //z["aap"] = "noot";
            //ConcurrentDictionary<string, List<int>> TitleFrames = new ConcurrentDictionary<string, List<int>>();
            //TitleFrames["14"] = new List<int>() { 1, 2, 3, 4 };
            //Dictionary<string, int> TitleFrames2 = new Dictionary<string, int>();
            //TitleFrames2["14"] = 15;

            //using (var f = File.Create(@"C:\temp\test.json"))
            //{
            //    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
            //    JsonSerializer.Serialize(w, TitleFrames);
            //    f.Close();
            //    needsSaving = false;
            //}
//            The following code registers the converter:

            var serializeOptions = new JsonSerializerOptions();
            serializeOptions.Converters.Add(new DiscChangerConverter());

            DiscChangers = File.Exists(discChangersJsonFileName) ? JsonSerializer.Deserialize<List<DiscChangerModel>>(new Utf8JsonReader(File.ReadAllBytes(discChangersJsonFileName)) : new List<DiscChangerModel>();
            discLookup = new MusicBrainz(Path.Combine(discsPath, "MusicBrainz"), discsRelPath+"/MusicBrainz");

            key2DiscChanger = new Dictionary<string, DiscChangerModel>(DiscChangers.Count);
            foreach (var discChanger in DiscChangers)
            {
                var discs = new ConcurrentDictionary<string, Disc>();
                if (discChanger.DiscList != null)
                {
                    foreach (var d in discChanger.DiscList)
                    {
                        d.DiscChanger = discChanger;
                        d.LookupData = discLookup.Get(d);
                        discs[d.Slot] = d;
                    }
                    discChanger.DiscList = null;
                }
                discChanger.Discs = discs;
                key2DiscChanger[discChanger.Key] = discChanger;
                try
                {
                    discChanger.Connect(this, _hubContext, _logger);
                }
                catch(Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Exception connecting disc changer: " + discChanger.Key + ": " + e.Message);
                    await _hubContext.Clients.All.SendAsync("StatusData",
                                   discChanger.Key,
                                   null,
                                   null,
                                   null,
                                   "off",
                                   null);
                }
            }
            //_timer = new Timer(DoWork, null, TimeSpan.Zero,
            //    TimeSpan.FromSeconds(5));
            _logger.LogInformation("Hosted service starting");

            await Task.Factory.StartNew(async () =>
            {
                // loop until a cancellation is requested
                int count = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Hosted service executing - {0}", DateTime.Now);
                    try
                    {
                        while (this.discDataMessages.TryTake(out Disc d, 3000, cancellationToken))
                        {
                            DiscChangerModel dc = d.DiscChanger;
                            try
                            {
                                MusicBrainz.Data mbd = discLookup.Lookup(d);
                                d.LookupData = mbd;
                                d.DateTimeAdded = DateTime.Now;
                                dc.Discs[d.Slot] = d;
                                needsSaving = true;

                                _hubContext.Clients.All.SendAsync("DiscData",
                                       dc.Key,
                                       d.Slot,
                                       d.toHtml(discLookup.musicBrainzArtRelPath));
                            }
                            catch(Exception e)
                            {
                                _logger.LogInformation(e, "Lookup failed {Key} {Slot}", dc.Key, d.Slot);
                            }
                        }
                        ++count;

                        if (needsSaving)
                            Save();
                        // wait for 3 seconds
//                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                }
            }, cancellationToken);
            //            return Task.CompletedTask;
        }

        internal void Move(string key, int offset)
        {
            lock (this.DiscChangers)
            {
                int i = this.DiscChangers.FindIndex(dc => dc.Key == key);
                if (i >= 0)
                {
                    int j = i + offset;
                    if (j >= 0 && j < DiscChangers.Count)
                    {
                        DiscChangerModel dc = DiscChangers[i];
                        DiscChangers[i] = DiscChangers[i + offset];
                        DiscChangers[i + offset] = dc;
                        Save();
                        _hubContext.Clients.All.SendAsync("Reload");
                    }
                }
            }
        }
        //internal void MoveDown(string key)
        //{
        //    Move(key, 1);
        //}

        //internal void MoveUp(string key)
        //{
        //    Move(key, -1);
        //}

        internal void Delete(string key)
        {
            lock (this.DiscChangers)
            {
                var discChanger = key2DiscChanger[key];
                discChanger.Disconnect();
                DiscChangers.Remove(discChanger);
                key2DiscChanger.Remove(key);
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }

        private void Save()
        {
            this._logger.LogInformation("Saving to " + discChangersJsonFileName);
            lock (this.DiscChangers)
            {
                foreach (var discChanger in DiscChangers)
                {
                    if (discChanger.Discs != null)
                    {
                        List<Disc> dl = new List<Disc>(discChanger.Discs.Count);
                        foreach (var kvp in discChanger.Discs)
                        {
                            dl.Add(kvp.Value);
                        }
                        discChanger.DiscList = dl.OrderBy(d => Int32.Parse(d.Slot)).ToList();
                    }
                }

                using (var f = File.Create(discChangersJsonFileName))
                {
                    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                    JsonSerializer.Serialize(w, DiscChangers, new JsonSerializerOptions { IgnoreNullValues = true });
                    f.Close();
                    needsSaving = false;
                }
                foreach (var discChanger in DiscChangers)
                    discChanger.DiscList = null;
            }
        }
        internal async Task<string> Test(string key, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl)
        {
            DiscChangerModel d = null;
            bool b = key!=null&&key2DiscChanger.TryGetValue(key, out d) && connection == d.Connection && portName == d.PortName;
            if (b)
                d.Disconnect();
            DiscChangerModel dc = new DiscChangerModel();
            try
            {
                dc.Type = type;
                dc.ReverseDiscExistBytes = (type == DiscChangerService.BDP_CX7000ES);
                dc.AdjustLastTrackLength = true;//This appears to be necessary for both CX777ES and CX7000ES.
                dc.Connection = connection;
                dc.CommandMode = commandMode;
                dc.PortName = portName;
                dc.HardwareFlowControl = HardwareFlowControl;
                dc.Connect(null, null, _logger);
                return await dc.Test();
            }
            catch (Exception e)
            {
                return $"Disc changer testing of ({key},{type},{connection},{commandMode},{portName},{HardwareFlowControl}) returned error: {e.Message}";
            }
            finally
            {
                dc.Disconnect();
                if (b)
                    d.Connect();
            }
        }
        internal void Add(string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl)
        {
            lock (this.DiscChangers)
            {
                string keyBase = new String(name.ToLower().Where(c => !char.IsWhiteSpace(c)).Take(6).ToArray());
                int i = 0;
                string key = keyBase;
                while( key2DiscChanger.ContainsKey(key))
                {
                    i++;
                    key = keyBase + i.ToString();
                }
                DiscChangerModel dc = new DiscChangerModel(key);
                dc.ReverseDiscExistBytes = (type == DiscChangerService.BDP_CX7000ES);
                dc.AdjustLastTrackLength = true;//This appears to be necessary for both CX777ES and CX7000ES.
                Update(dc, name, type, connection, commandMode, portName, HardwareFlowControl);
                DiscChangers.Add(dc);
                key2DiscChanger[key] = dc;
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(string key, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl )
        {
            lock (this.DiscChangers)
            {
                Update(key2DiscChanger[key], name, type, connection, commandMode, portName, HardwareFlowControl);
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(DiscChangerModel discChanger, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl)
        {
            lock (this.DiscChangers)
            {
                if (DiscChangers.Any(dc =>dc!=discChanger && dc.Name==name))
                    throw new Exception($"Name {name} already exists");
                if (DiscChangers.Any(dc=>dc!=discChanger && dc.Connection==connection && dc.PortName==portName ))
                    throw new Exception($"Connection {connection}:{portName} already in use");
                discChanger.Name = name;
                discChanger.Type = type;
                if (discChanger.Connection != connection || 
                    discChanger.PortName != portName ||
                    discChanger.CommandMode != commandMode ||
                    discChanger.HardwareFlowControl != HardwareFlowControl )
                {
                    discChanger.Disconnect();
                    //discChanger.Protocol   = protocol;
                    discChanger.Connection  = connection;
                    discChanger.CommandMode = commandMode;
                    discChanger.PortName    = portName;
                    discChanger.HardwareFlowControl = HardwareFlowControl;
                    discChanger.Connect(this, this._hubContext, _logger);
                }
            }
        }

        public DiscChangerModel Changer(string changerKey)
        {
            return key2DiscChanger[changerKey];
        }
        private int executionCount=0;
        public bool needsSaving = false;

        //private void DoWork(object state)
        //{
        //    var count = Interlocked.Increment(ref executionCount);

        //    _logger.LogInformation(
        //        "Timed Hosted Service is working. Count: {Count}", count);
        //    string msg = $"Timed Hosted Service is working. Count: {count}";
        //    System.Diagnostics.Debug.WriteLine(msg);
        //    _hubContext.Clients.All.SendAsync("ReceiveMessage", "test", msg);
        //}

        public override Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            //_timer?.Change(Timeout.Infinite, 0);
            foreach (var dc in this.DiscChangers)
                dc.Disconnect();
//            mySerialPort.Close();

            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            if(DiscChangers!=null)
                foreach (var dc in this.DiscChangers)
                    dc.Disconnect();
            _timer?.Dispose();
        }
    }
}
