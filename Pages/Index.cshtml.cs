/*  Copyright 2020 Hugo Lyppens

    Index.cshtml.cs is part of DiscChanger.NET.

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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using DiscChanger.Models;

namespace DiscChanger.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        public DiscChangerService discChangerService;

        public IndexModel(DiscChangerService discChangerService, ILogger<IndexModel> logger)
        {
            this.discChangerService = discChangerService;
            _logger = logger;
        }

        public void OnGet()
        {

        }

        public JsonResult OnGetDiscsToScan(string changerKey)
        {
            string discSet;
            try
            {
                discSet = discChangerService.Changer(changerKey).getDiscsToScan();
            }
            catch(Exception e)
            {
                discSet = "Error: " + e.Message;
            }
            return new JsonResult(discSet);
        }
        public JsonResult OnGetDiscsToDelete(string changerKey)
        {
            string discSet;
            try
            {
                discSet = discChangerService.Changer(changerKey).getDiscsToDelete();
            }
            catch (Exception e)
            {
                discSet = "Error: " + e.Message;
            }
            return new JsonResult(discSet);
        }
        public JsonResult OnGetPopulatedSlots(string changerKey, string slotsSet)
        {
            string discSet;
            try
            {
                discSet = discChangerService.Changer(changerKey).getPopulatedSlots(slotsSet);
            }
            catch (Exception e)
            {
                discSet = "";
            }
            return new JsonResult(discSet);
        }

        public JsonResult OnGetValidateDiscShift( string changerKey,
                        string populatedSlotsSet,
                        int offset)
        {
            string discSet;
            try
            {
                discSet = discChangerService.Changer(changerKey).getDiscsShiftDestination(populatedSlotsSet, offset);
            }
            catch (Exception e)
            {
                discSet = "";
            }
            return new JsonResult(discSet);
        }

    }
}
