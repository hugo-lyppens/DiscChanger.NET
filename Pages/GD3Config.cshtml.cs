/*  Copyright 2020 Hugo Lyppens

    GD3.cshtml.cs is part of DiscChanger.NET.

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
using System.Text;

namespace DiscChanger.Pages
{
    [BindProperties(SupportsGet = true)]
    public class GD3Config : PageModel
    {
        public Dictionary<string, object> OnChangeSubmit = new Dictionary<string, object> { { "onchange", "document.changer.submit()" } };
        private IConfiguration _config;
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool AutoCDLookup { get; set; }
        public bool AutoDVDLookup { get; set; }
        public bool AutoBDLookup { get; set; }
        public string Message = null;

        private readonly DiscChangerService discChangerService;
        private MetaDataGD3 metaDataGD3;
        public MetaDataGD3 GetMetaDataGD3() { return metaDataGD3; }
        public GD3Config(IConfiguration config, DiscChangerService dcs)
        {
            _config = config;
            discChangerService = dcs;
            metaDataGD3 = dcs.GetMetaDataGD3();
            var settings = metaDataGD3.GetSettings();
            if(settings !=null)
            { 
                AutoCDLookup = settings.AutoCDLookup;
                AutoDVDLookup = settings.AutoDVDLookup;
                AutoBDLookup = settings.AutoBDLookup;
            }
        }
        public IActionResult OnGet()
        {
            return Page();
        }
        public async Task<IActionResult> OnPostSetCredentialsAsync()
        {
            try
            {
                GD3.AuthHeader authCD = new GD3.AuthHeader() { Password = this.Password, Username = this.UserName };
                GD3DVD.AuthHeader authDVD = new GD3DVD.AuthHeader() { Password = this.Password, Username = this.UserName };
                var l = await metaDataGD3.GetNumberLookupsRemainingAsync(authCD, authDVD);

                StringBuilder sb = new StringBuilder();
                sb.Append("CD number of lookups remaining query => ");
                if (l.CD.HasValue)
                {
                    sb.Append("Success ("); sb.Append(l.CD.Value); sb.Append(")\n");
                }
                else
                {
                    sb.Append("Error: "); sb.Append(l.ErrorCD);
                }
                sb.Append('\n');
                sb.Append("DVD number of lookups remaining query => ");
                if (l.DVD.HasValue)
                {
                    sb.Append("Success ("); sb.Append(l.DVD.Value); sb.Append(")\n");
                }
                else
                {
                    sb.Append("Error: "); sb.Append(l.ErrorDVD);
                }
                sb.Append('\n');
                if (l.CD == null && l.DVD == null)
                    throw new Exception("GD3 no access to CD / DVD services with these credentials");
                metaDataGD3.SetCredentials(UserName, Password);
                sb.Append("Credentials Saved");
                metaDataGD3.CurrentLookupsRemaining = l;
                Message = sb.ToString();
            }
            catch (Exception e)
            {
                Message += "\nException:" + e.Message;
            }

            return Page();
        }
        public IActionResult OnPostClearCredentials()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                metaDataGD3.SetCredentials(null,null);
                sb.Append("Credentials Cleared");
                metaDataGD3.CurrentLookupsRemaining = null;
                Message = sb.ToString();
            }
            catch (Exception e)
            {
                Message += "\nException:" + e.Message;
            }
            return Page();
        }
        public IActionResult OnPostClose()
        {
            var settings=metaDataGD3.GetSettings();
            bool changed = false;
            if (settings.AutoCDLookup != AutoCDLookup)
            {
                settings.AutoCDLookup = AutoCDLookup; changed = true;
            }
            if (settings.AutoDVDLookup != AutoDVDLookup)
            {
                settings.AutoDVDLookup = AutoDVDLookup; changed = true;
            }
            if (settings.AutoBDLookup != AutoBDLookup)
            {
                settings.AutoBDLookup = AutoBDLookup; changed = true;
            }
            if(changed)
                metaDataGD3.SaveSettings();
            return RedirectToPage("Index");
        }
    }
}
