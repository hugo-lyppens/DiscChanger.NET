﻿/*  Copyright 2020 Hugo Lyppens

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

namespace DiscChanger.Models
{
    //    public class DiscChanger : IHostedService, IDisposable
    public class DiscChangerService : BackgroundService
    {
        public List<DiscChangerModel> DiscChangers { get; private set; }
        public MusicBrainz discLookup;


        private Dictionary<string, DiscChangerModel> key2DiscChanger;

        private readonly ILogger<DiscChangerService> _logger;

        private readonly IHubContext<DiscChangerHub> _hubContext;
        private string webRootPath, discChangersJsonFileName, discsPath, discsRelPath;
        BufferBlock<Disc> discDataMessages = new BufferBlock<Disc>();
        public void AddDiscData(Disc d ) { discDataMessages.Post(d); }
        public DiscChangerService(IWebHostEnvironment environment, IHubContext<DiscChangerHub> hubContext, ILogger<DiscChangerService> logger)
        {
            _logger = logger;
            _hubContext = hubContext;
            webRootPath = environment.WebRootPath;
            discChangersJsonFileName = Path.Combine(webRootPath, "DiscChangers.json");
            discsRelPath = "Discs";
            discsPath    = Path.Combine(webRootPath, discsRelPath );
        }
        private void Load()
        {
            this._logger.LogInformation("Loading from " + discChangersJsonFileName);
            DiscChangers = File.Exists(discChangersJsonFileName) ? JsonSerializer.Deserialize<List<DiscChangerModel>>(File.ReadAllBytes(discChangersJsonFileName)) : new List<DiscChangerModel>();
            //using (var f = File.Create(@"C:\Temp\dc.json"))
            //{
            //    //var discs = DiscChangers[2].Discs;
            //    //for (int i = 0; i < 55; i++)
            //    //{
            //    //    string oldSlot = (345 + i).ToString();
            //    //    string newSlot = (290 + i).ToString();
            //    //    if (discs.TryRemove(oldSlot, out Disc d))
            //    //    {
            //    //        d.Slot = newSlot;
            //    //        discs[newSlot] = d;
            //    //    }
            //    //}
            //    var w = new Utf8JsonWriter(f, new JsonWriterOptions { Indented = true });
            //    JsonSerializer.Serialize(w, DiscChangers, new JsonSerializerOptions { IgnoreNullValues = true });
            //    f.Close();
            //}
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

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hosted Service running.");
            System.Diagnostics.Debug.WriteLine("Hosted Service running.");

            Load();
            discLookup = new MusicBrainz(Path.Combine(discsPath, "MusicBrainz"), discsRelPath+"/MusicBrainz");

            key2DiscChanger = new Dictionary<string, DiscChangerModel>(DiscChangers.Count);
            List<Task> connectTasks = new List<Task>(DiscChangers.Count);
            foreach (var discChanger in DiscChangers)
            {
                key2DiscChanger[discChanger.Key] = discChanger;
                foreach (var kvp in discChanger.Discs)
                {
                    Disc d = kvp.Value;
                    d.LookupData = discLookup.Get(d);
                }
                try
                {
                    connectTasks.Add(discChanger.Connect(this, _hubContext, _logger));
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
            Task.WaitAll(connectTasks.ToArray());
            _logger.LogInformation("Hosted service starting");
            TimeSpan discDataTimeOut = TimeSpan.FromSeconds(3);
            const int NullStatusQueryFrequency = 40;//only query null status every 40x3 seconds
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
                        DiscChangerModel dc = d.DiscChanger;
                        try
                        {
                            MusicBrainz.Data mbd = discLookup.Lookup(d);
                            d.LookupData = mbd;
                            d.DateTimeAdded ??= DateTime.Now;
                            dc.Discs[d.Slot] = d;
                            needsSaving = true;

                            await _hubContext.Clients.All.SendAsync("DiscData",
                                    dc.Key,
                                    d.Slot,
                                    d.toHtml());
                        }
                        catch (Exception e)
                        {
                            _logger.LogInformation(e, "Lookup failed {Key} {Slot}", dc.Key, d.Slot);
                        }
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
                            if((status == null && countDown == 0)/*||(status!=null&&status.IsOutDated())*/) //feature to refresh outdated status prevents auto off on DVP-CX777ES
                            {
                                discChanger.ClearStatus();
                                if(discChanger.Connected())
                                    discChanger.InitiateStatusUpdate();
                            }
                        }
                        if (countDown <= 0)
                            countDown = NullStatusQueryFrequency;
                    }
                    catch (Exception e) {
                        System.Diagnostics.Debug.WriteLine( "Hosted Service Exception: "+e.Message);
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
                        DiscChangerModel dc = DiscChangers[i];
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

        internal async Task<string> Test(string key, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkAddress)
        {
            DiscChangerModel d = null;
            bool b = key!=null&&key2DiscChanger.TryGetValue(key, out d) && connection == d.Connection && portName == d.PortName && networkAddress == d.NetworkHost;
            if (b)
                d.Disconnect();
            DiscChangerModel dc = DiscChangerModel.Create(type);
            try
            {
                dc.Connection = connection;
                dc.CommandMode = commandMode;
                dc.PortName = portName;
                dc.HardwareFlowControl = HardwareFlowControl;
                dc.NetworkHost = networkAddress;
                await dc.Connect(null, null, _logger);
                return await dc.Test();
            }
            catch (Exception e)
            {
                return $"Disc changer testing of ({key},{type},{connection},{commandMode},{portName},{HardwareFlowControl},{networkAddress}) returned error: {e.Message}";
            }
            finally
            {
                dc.Disconnect();
                if (b)
                    await d.Connect();
            }
        }
        internal void Add(string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkAddress)
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
                DiscChangerModel dc = DiscChangerModel.Create(type);dc.Key = key;
                Update(dc, name, type, connection, commandMode, portName, HardwareFlowControl, networkAddress);
                DiscChangers.Add(dc);
                key2DiscChanger[key] = dc;
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(string key, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkAddress )
        {
            lock (this.DiscChangers)
            {
                Update(key2DiscChanger[key], name, type, connection, commandMode, portName, HardwareFlowControl, networkAddress);
                Save();
                _hubContext.Clients.All.SendAsync("Reload");
            }
        }
        internal void Update(DiscChangerModel discChanger, string name, string type, string connection, string commandMode, string portName, bool? HardwareFlowControl, string networkAddress )
        {
            lock (this.DiscChangers)
            {
                if (DiscChangers.Any(dc =>dc!=discChanger && dc.Name==name))
                    throw new Exception($"Name {name} already exists");
                if (DiscChangers.Any(dc=>dc!=discChanger && dc.Connection==connection && dc.PortName==portName &&dc.NetworkHost==networkAddress))
                    throw new Exception($"Connection {connection}:{portName} {networkAddress} already in use");
                discChanger.Name = name;
                discChanger.Type = type;
                if (discChanger.Connection != connection || 
                    discChanger.PortName != portName ||
                    discChanger.CommandMode != commandMode ||
                    discChanger.HardwareFlowControl != HardwareFlowControl ||
                    discChanger.NetworkHost != networkAddress )
                {
                    discChanger.Disconnect();
                    //discChanger.Protocol   = protocol;
                    discChanger.Connection  = connection;
                    discChanger.CommandMode = commandMode;
                    discChanger.PortName    = portName;
                    discChanger.HardwareFlowControl = HardwareFlowControl;
                    discChanger.NetworkHost = networkAddress;
                    discChanger.Connect(this, this._hubContext, _logger);
                }
            }
        }

        public DiscChangerModel Changer(string changerKey)
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
            if(DiscChangers!=null)
                foreach (var dc in this.DiscChangers)
                    dc.Disconnect();
        }
    }
}
