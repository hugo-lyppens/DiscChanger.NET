/*  Copyright 2020 Hugo Lyppens

    DiscChangerHub.cs is part of DiscChanger.NET.

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
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using DiscChanger.Models;

namespace DiscChanger.Hubs
{
    public class DiscChangerHub : Hub
    {
        public DiscChangerService discChangerService;
        private readonly Microsoft.Extensions.Logging.ILogger<DiscChangerHub> _logger;

        public DiscChangerHub(DiscChangerService discChangerService, ILogger<DiscChangerHub> logger)
        {
            _logger = logger;
            this.discChangerService = discChangerService;
        }

        public void Control(string changerKey, string command)
        {
            try
            {
                _ = discChangerService.Changer(changerKey).ControlAsync(command);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from control " + changerKey + '/' + command + ": " + e);
            }
        }
        public async Task DiscDirectAsync(string changerKey, int? discNumber, int? titleAlbumNumber, int? chapterTrackNumber)
        {
            try
            {
                var dcs = (DiscChangerSony)(discChangerService.Changer(changerKey));
                bool b = await dcs.DiscDirect(discNumber, titleAlbumNumber, chapterTrackNumber);
                if (!b)
                    System.Diagnostics.Debug.WriteLine("False return from DiscDirect " + changerKey + '/' + Convert.ToString(discNumber) + '/' + Convert.ToString(titleAlbumNumber) + '/' + Convert.ToString(chapterTrackNumber));

            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from DiscDirect " + changerKey + '/' + Convert.ToString(discNumber) + '/' + Convert.ToString(titleAlbumNumber) + '/' + Convert.ToString(chapterTrackNumber) + ": " + e.Message);
            }
        }
        public void Scan(string changerKey, string discSet)
        {
            try
            {
                _ = discChangerService.Changer(changerKey).Scan(discSet);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from Scan " + changerKey + '/' + discSet + ": " + e.Message);
            }
        }
        public void RetrieveMetaData(string changerKey, string metaDataType, string discSet)
        {
            try
            {
                _ = discChangerService.Changer(changerKey).RetrieveMetaDataAsync(metaDataType, discSet);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from metadata retrieval" + changerKey + '/' + metaDataType + '/' + discSet + ": " + e.Message);
            }
        }
        public void CancelOp(string changerKey)
        {
            try
            {
                discChangerService.Changer(changerKey).CancelOp();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from Cancel Scan " + changerKey + ": " + e.Message);
            }
        }
        public async Task DeleteDiscs(string changerKey, string discSet)
        {
            try
            {
                await discChangerService.Changer(changerKey).DeleteDiscs(discSet);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from Delete " + changerKey + '/' + discSet + ": " + e.Message);
            }
        }
        public void DeleteChanger(string changerKey)
        {
            try
            {
                discChangerService.Delete(changerKey);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from Delete Changer " + changerKey + ": " + e.Message);
            }
        }
        public void MoveChanger(string changerKey, int offset)
        {
            try
            {
                discChangerService.Move(changerKey, offset);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from Delete Changer " + changerKey + ": " + e.Message);
            }
        }
        public async Task ShiftDiscs(string changerKey, string sourceSlotsSet, string destinationSlotsSet)
        {
            try
            {
                await discChangerService.Changer(changerKey).ShiftDiscs(sourceSlotsSet, destinationSlotsSet);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception from ShiftDiscs " + changerKey + '/' + sourceSlotsSet + '/' + destinationSlotsSet + ": " + e.Message);
            }
        }

    }
}
