﻿/*  Copyright 2020 Hugo Lyppens

    DiscChangerConfig.cshtml.cs is part of DiscChanger.NET.

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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using DiscChanger.Models;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace DiscChanger.Pages
{
    [BindProperties(SupportsGet = true)]
    public class DiscChangerConfig : PageModel
    {
        public Dictionary<string, object> OnChangeSubmit = new Dictionary<string, object> { { "onchange", "document.changer.submit()" } };
        private IConfiguration _config;
        [FromQuery]
        public string Key { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Connection { get; set; }
        public string PortName { get; set; }
        public string NetworkHost { get; set; }
        public int? NetworkPort { get; set; }
        public string CommandMode { get; set; }
        public string Save { get; set; }
        public string Cancel { get; set; }
        public IEnumerable<string> SerialPortNames;// = new string[] { };
        public IEnumerable<string> ConnectionTypes;// = null;// new string[] { };
        public string TestResult = null;
        public string HardwareFlowControl { get; set; }

        private DiscChangerService discChangerService;
        public DiscChangerConfig(IConfiguration config, DiscChangerService dcs)
        {
            _config = config;
            discChangerService = dcs;
        }
        private void updateModel(string Key)
        {
            DiscChanger.Models.DiscChanger discChanger = null;
            if (Key != null)
            {
                discChanger = discChangerService.Changer(Key);

                Name ??= discChanger.Name;
                Type ??= discChanger.Type;
                Connection ??= discChanger.Connection;
                CommandMode ??= discChanger.CommandMode;
                PortName ??= discChanger.PortName;
                NetworkHost ??= discChanger.NetworkHost;
                NetworkPort ??= discChanger.NetworkPort;
                HardwareFlowControl ??= discChanger.HardwareFlowControl?.ToString();
            }
            switch (Type)
            {
                case DiscChangerSonyDVD.DVP_CX777ES:
                    CommandMode = null;
                    Connection = DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT;
                    ConnectionTypes = new string[] { DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT }; break;
                case DiscChangerSonyBD.BDP_CX7000ES:
                    CommandMode ??= DiscChangerSonyBD.CommandModes[0];
                    Connection ??= DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT;
                    ConnectionTypes = new string[] { DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT, DiscChanger.Models.DiscChanger.CONNECTION_NETWORK }; break;
            }
            SerialPortNames = null;
            if (Connection == DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT)
            {
                HardwareFlowControl ??= "true";
                SerialPortNames = Array.FindAll(SerialPort.GetPortNames(), p => discChangerService.DiscChangers.All(dc => dc == discChanger || dc.PortName != p));
            }
            else if (Connection == DiscChanger.Models.DiscChanger.CONNECTION_NETWORK)
            {
                NetworkPort ??= 6001;
                SerialPortNames = null;
            }
        }
        //        public async Task<IActionResult> OnGetAsync()
        public IActionResult OnGet()
        {
            if (Key != null)
                updateModel(Key);
            return Page();
        }
        public async Task<IActionResult> OnPostAsync(string op = null)
        {
            bool? hfc = null;
            string pn = null;
            string nh = null;
            int? np = null;
            bool valid = !String.IsNullOrEmpty(Type) && !String.IsNullOrEmpty(Name) && !String.IsNullOrEmpty(Connection);
            switch (Connection)
            {
                case DiscChanger.Models.DiscChanger.CONNECTION_SERIAL_PORT:
                    if (HardwareFlowControl != null)
                        hfc = Boolean.Parse(HardwareFlowControl);
                    pn = PortName;
                    valid = valid && !String.IsNullOrEmpty(pn);
                    break;
                case DiscChanger.Models.DiscChanger.CONNECTION_NETWORK:
                    nh = NetworkHost?.Trim(); np = NetworkPort;
                    valid = valid && !String.IsNullOrEmpty(nh) && np > 1;
                    break;
            }
            switch (op)
            {
                case "OK":
                    if (valid)
                    {
                        if (!String.IsNullOrEmpty(Key))
                            discChangerService.Update(Key, Name, Type, Connection, CommandMode, pn, hfc, nh, np);
                        else
                            discChangerService.Add(Name, Type, Connection, CommandMode, pn, hfc, nh, np);
                        return RedirectToPage("Index");
                    }
                    break;
                case "Cancel":
                    return RedirectToPage("Index");
                case "Test":
                    if (valid)
                        this.TestResult = await discChangerService.Test(Key, Type, Connection, CommandMode, pn, hfc, nh, np);
                    break;
            }
            updateModel(Key);
            return Page();

        }
    }
}
