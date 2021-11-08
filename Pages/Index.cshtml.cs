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
using System.Collections.Generic;
using System.Threading.Tasks;

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


        public async Task<IActionResult> OnGetDiscsToScanAsync(string changerKey)
        {
            try
            {
                var t = await discChangerService.Changer(changerKey).GetDiscsToScan();
                return new JsonResult(new { discsToScan = t.Item1, emptySlots = t.Item2 });
            }
            catch (Exception e)
            {
                return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError, e.Message);
            }
        }
        public async Task<IActionResult> OnGetDiscsToDeleteAsync(string changerKey)
        {
            try
            {
                var t = await discChangerService.Changer(changerKey).GetDiscsToDelete();
                return new JsonResult(new { discsToDelete = t.Item1, emptySlots = t.Item2 });
            }
            catch (Exception e)
            {
                return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError, e.Message);
            }
        }
        private IActionResult exceptionAsError<T>(Func<T> f)
        {
            try
            {
                return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status200OK, f());
            }
            catch (Exception e)
            {
                return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status500InternalServerError, e.Message);
            }
        }
        public IActionResult OnGetPopulatedSlots(string changerKey, string slotsSet)
        {
            return exceptionAsError(() => discChangerService.Changer(changerKey).GetPopulatedSlots(slotsSet));
        }
        public IActionResult OnGetAvailableSlots(string changerKey)
        {
            return exceptionAsError(() => discChangerService.Changer(changerKey).GetAvailableSlots());
        }

        public IActionResult OnGetValidateDiscShift(string changerKey,
                        string slotsSet,
                        int offset)
        {
            return exceptionAsError(() => discChangerService.Changer(changerKey).GetDiscsShiftDestination(slotsSet, offset));
        }
        public IActionResult OnGetMetaDataNeeded(string changerKey, string metaDataType)
        {
            return exceptionAsError(() => discChangerService.Changer(changerKey).GetMetaDataNeeded(metaDataType));
        }
    }
}
