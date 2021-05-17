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
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.DataProtection;

namespace DiscChanger.Models
{
    //    public class DiscChanger : IHostedService, IDisposable
    public class DiscChangerService : BackgroundService
    {
        public List<DiscChanger> DiscChangers { get; private set; }
        private MetaDataMusicBrainz metaDataMusicBrainz;
        private MetaDataGD3 metaDataGD3;
        public MetaDataGD3 GetMetaDataGD3() { return metaDataGD3; }


        private Dictionary<string, DiscChanger> key2DiscChanger;

        private readonly ILogger<DiscChangerService> _logger;

        //        private IDataProtector _protector;
        private IDataProtectionProvider _provider;
        private readonly IHubContext<DiscChangerHub> _hubContext;
        private string webRootPath, contentRootPath, discChangersJsonFileName, discsPath, discsRelPath;
        BufferBlock<Disc> discDataMessages = new BufferBlock<Disc>();
        public void AddDiscData(Disc d) { discDataMessages.Post(d); }
        public DiscChangerService(
            IWebHostEnvironment environment,
            IHubContext<DiscChangerHub> hubContext,
            IDataProtectionProvider provider,
            ILogger<DiscChangerService> logger)
        {
            _logger = logger;
            _hubContext = hubContext;
            _provider = provider;
            contentRootPath = environment.ContentRootPath;
            webRootPath = environment.WebRootPath;
            discChangersJsonFileName = Path.Combine(webRootPath, "DiscChangers.json");
            discsRelPath = "Discs";
            discsPath = Path.Combine(webRootPath, discsRelPath);
        }
        private void Load()
        {
            this._logger.LogInformation("Loading from " + discChangersJsonFileName);
            DiscChangers = File.Exists(discChangersJsonFileName) ? JsonSerializer.Deserialize<List<DiscChanger>>(File.ReadAllBytes(discChangersJsonFileName)) : new List<DiscChanger>();
            needsSaving = false;
        }
        private void Save()
        {
            this._logger.LogInformation("Saving to " + discChangersJsonFileName);
            lock (this.DiscChangers)
            {
                using (var f = File.Create(discChangersJsonFileName))
                {
                    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
                    JsonSerializer.Serialize(w, DiscChangers, new JsonSerializerOptions { IgnoreNullValues = true });
                    f.Close();
                    needsSaving = false;
                }
            }
        }
        static readonly string[] OnlyMusicBrainz = { MetaDataMusicBrainz.Type };
        static readonly string[] MusicBrainzAndGD3 = { MetaDataMusicBrainz.Type, MetaDataGD3.Type_Match, MetaDataGD3.Type_MetaData };
        public IEnumerable<string> GetAvailableMetaDataServices()
        {
            return metaDataGD3?.CurrentLookupsRemaining == null ? OnlyMusicBrainz : MusicBrainzAndGD3;
        }

        public async Task UpdateMetaData(Disc d, HashSet<string> metaDataTypes)
        {
            bool changed = false;
            DiscChanger dc = d.DiscChanger;
            System.Diagnostics.Debug.WriteLine($"About to Lookup {String.Join(',', metaDataTypes)} {dc?.Key} {d.Slot}");
            if (metaDataMusicBrainz != null && metaDataTypes.Contains(MetaDataMusicBrainz.Type))
            {
                try
                {
                    MetaDataMusicBrainz.Data mbd = await metaDataMusicBrainz.RetrieveMetaData(d);
                    if (mbd != null && d.DataMusicBrainz != mbd)
                    {
                        d.DataMusicBrainz = mbd; changed = true;
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"MusicBrainz Lookup failed {dc.Key} {d.Slot}: {e.Message}");
                    _logger.LogInformation(e, "MusicBrainz Lookup failed {Key} {Slot}", dc.Key, d.Slot);
                }
            }
            if (metaDataGD3 != null)
            {
                MetaDataGD3.Match gd3d = d.DataGD3Match;
                try
                {

                    if (metaDataTypes.Contains(MetaDataGD3.Type_Match))
                    {
                        gd3d = await metaDataGD3.RetrieveMatch(d);
                        if (gd3d != null && d.DataGD3Match != gd3d)
                        {
                            d.DataGD3Match = gd3d; changed = true;
                        }
                    }
                    if (gd3d != null && !gd3d.HasMetaData() && metaDataTypes.Contains(MetaDataGD3.Type_MetaData))
                    {
                        if (await gd3d.RetrieveMetaData())
                            changed = true;
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"GD3 Match failed {dc.Key} {d.Slot}: {e.Message}");
                    _logger.LogInformation(e, "GD3 Match failed {Key} {Slot}", dc.Key, d.Slot);
                }
            }

            if (dc != null && changed)
            {
                await _hubContext.Clients.All.SendAsync("DiscData",
                        dc.Key,
                        d.Slot,
                        d.ToHtml());
            }
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hosted Service running.");
            System.Diagnostics.Debug.WriteLine("Hosted Service running.");
            Load();
            metaDataMusicBrainz = new MetaDataMusicBrainz(Path.Combine(discsPath, "MusicBrainz"), discsRelPath + "/MusicBrainz");
            metaDataGD3 = new MetaDataGD3(_provider, contentRootPath, Path.Combine(discsPath, "GD3"), discsRelPath + "/GD3");

            key2DiscChanger = new Dictionary<string, DiscChanger>(DiscChangers.Count);
            List<Task> connectTasks = new List<Task>(DiscChangers.Count);
            foreach (var discChanger in DiscChangers)
            {
                key2DiscChanger[discChanger.Key] = discChanger;
                foreach (var kvp in discChanger.Discs)
                {
                    Disc d = kvp.Value;
                    d.DataMusicBrainz = metaDataMusicBrainz.Get(d);
                    d.DataGD3Match = metaDataGD3.Get(d);
                }
                try
                {
                    connectTasks.Add(discChanger.Connect(this, _hubContext, _logger));
                }
                catch (Exception e)
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
            Task.WaitAll(connectTasks.ToArray());
            _logger.LogInformation("Hosted service starting");
            TimeSpan discDataTimeOut = TimeSpan.FromSeconds(3);
            const int NullStatusQueryFrequency = 40;//only query null status every 40x3 seconds
            var metaDataSet = new HashSet<string> { MetaDataMusicBrainz.Type, MetaDataGD3.Type_Match };
            var metaDataSetWithGD3Lookup = new HashSet<string> { MetaDataMusicBrainz.Type, MetaDataGD3.Type_Match, MetaDataGD3.Type_MetaData };

            await Task.Factory.StartNew(async () =>
            {
                int countDown = NullStatusQueryFrequency;
                // loop until a cancellation is requested
                while (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Hosted service executing - {0}", DateTime.Now);
                    try
                    {
                        Disc d = await discDataMessages.ReceiveAsync(discDataTimeOut, cancellationToken);
                        DiscChanger dc = d.DiscChanger;
                        await UpdateMetaData(d, (metaDataGD3?.AutoLookupEnabled(d) ?? false) ? metaDataSetWithGD3Lookup : metaDataSet);
                        d.DateTimeAdded ??= DateTime.Now;
                        dc.Discs[d.Slot] = d;
                        needsSaving = true;

                    }
                    catch (OperationCanceledException) { }
                    catch (TimeoutException)
                    {
                        if (needsSaving)
                            Save();
                        countDown--;
                        foreach (var discChanger in DiscChangers)
                        {
                            var status = discChanger.CurrentStatus();
                            if ((status == null && countDown == 0)/*||(status!=null&&status.IsOutDated())*/) //feature to refresh outdated status prevents auto off on DVP-CX777ES
                            {
                                discChanger.ClearStatus();
                                if (discChanger.Connected())
                                    discChanger.InitiateStatusUpdate();
                            }
                        }
                        if (countDown <= 0)
                            countDown = NullStatusQueryFrequency;
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine("Hosted Service Exception: " + e.Message);
                        _logger.LogInformation(e, "Hosted Service Exception");
                    }
                }
            }, cancellationToken);
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
                        DiscChanger dc = DiscChangers[i];
                        DiscChangers[i] = DiscChangers[i + offset];
                        DiscChangers[i + offset] = dc;
                        Save();
                        _hubContext.Clients.All.SendAsync("Reload");
                    }
                }
            }
        }

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

        internal async Task<string> Test(string key, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkHost, int? networkPort)
        {
            DiscChanger d = null;
            bool b = key != null && key2DiscChanger.TryGetValue(key, out d) && connection == d.Connection && portName == d.PortName && networkHost == d.NetworkHost && networkPort == d.NetworkPort;
            if (b)
                d.Disconnect();
            DiscChanger dc = DiscChanger.Create(type);
            try
            {
                dc.Connection = connection;
                dc.CommandMode = commandMode;
                dc.PortName = portName;
                dc.HardwareFlowControl = HardwareFlowControl;
                dc.NetworkHost = networkHost;
                dc.NetworkPort = networkPort;
                await dc.Connect(null, null, _logger);
                return await dc.Test();
            }
            catch (Exception e)
            {
                return $"Disc changer testing of ({key},{type},{connection},{commandMode},{portName},{HardwareFlowControl},{networkHost}) returned error: {e.Message}";
            }
            finally
            {
                dc.Disconnect();
                if (b)
                    await d.Connect();
            }
        }
        internal void Add(string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkHost, int? networkPort)
        {
            lock (this.DiscChangers)
            {
                string keyBase = new String(name.ToLower().Where(c => !char.IsWhiteSpace(c)).Take(6).ToArray());
                int i = 0;
                string key = keyBase;
                while (key2DiscChanger.ContainsKey(key))
                {
                    i++;
                    key = keyBase + i.ToString();
                }
                DiscChanger dc = DiscChanger.Create(type); dc.Key = key;
                Update(dc, name, type, connection, commandMode, portName, HardwareFlowControl, networkHost, networkPort);
                DiscChangers.Add(dc);
                key2DiscChanger[key] = dc;
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(string key, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkHost, int? networkPort)
        {
            lock (this.DiscChangers)
            {
                Update(key2DiscChanger[key], name, type, connection, commandMode, portName, HardwareFlowControl, networkHost, networkPort);
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(DiscChanger discChanger, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkHost, int? networkPort)
        {
            lock (this.DiscChangers)
            {
                if (DiscChangers.Any(dc => dc != discChanger && dc.Name == name))
                    throw new Exception($"Name {name} already exists");
                if (DiscChangers.Any(dc => dc != discChanger && dc.Connection == connection && dc.PortName == portName && dc.NetworkHost == networkHost && dc.NetworkPort == networkPort))
                    throw new Exception($"Connection {connection}:{portName} {networkHost} already in use");
                discChanger.Name = name;
                discChanger.Type = type;
                if (discChanger.Connection != connection ||
                    discChanger.PortName != portName ||
                    discChanger.CommandMode != commandMode ||
                    discChanger.HardwareFlowControl != HardwareFlowControl ||
                    discChanger.NetworkHost != networkHost ||
                    discChanger.NetworkPort != networkPort)
                {
                    discChanger.Disconnect();
                    //discChanger.Protocol   = protocol;
                    discChanger.Connection = connection;
                    discChanger.CommandMode = commandMode;
                    discChanger.PortName = portName;
                    discChanger.HardwareFlowControl = HardwareFlowControl;
                    discChanger.NetworkHost = networkHost;
                    discChanger.NetworkPort = networkPort;
                    discChanger.Connect(this, this._hubContext, _logger);
                }
            }
        }

        public DiscChanger Changer(string changerKey)
        {
            return key2DiscChanger[changerKey];
        }

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
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            if (DiscChangers != null)
                foreach (var dc in this.DiscChangers)
                    dc.Disconnect();
        }
    }
}