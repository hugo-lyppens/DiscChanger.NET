using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace DiscChanger.Models
{
    /// <summary>
    /// Service for managing custom discs stored separately from scanned discs
    /// </summary>
    public class CustomDiscService
    {
        private readonly ILogger<CustomDiscService> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string _customDiscsFilePath;
        private CustomDiscCollection _customDiscs;
        private readonly object _lockObject = new object();

        public CustomDiscService(ILogger<CustomDiscService> logger, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _customDiscsFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "CustomDiscs.json");
            LoadCustomDiscs();
        }

        private void LoadCustomDiscs()
        {
            try
            {
                if (File.Exists(_customDiscsFilePath))
                {
                    string json = File.ReadAllText(_customDiscsFilePath);
                    _customDiscs = JsonSerializer.Deserialize<CustomDiscCollection>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    _logger.LogInformation($"Loaded {_customDiscs.CustomDiscs.Count} custom discs from {_customDiscsFilePath}");
                }
                else
                {
                    _customDiscs = new CustomDiscCollection();
                    _logger.LogInformation("No custom discs file found, starting with empty collection");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom discs");
                _customDiscs = new CustomDiscCollection();
            }
        }

        public async Task SaveCustomDiscs()
        {
            try
            {
                lock (_lockObject)
                {
                    string json = JsonSerializer.Serialize(_customDiscs, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_customDiscsFilePath, json);
                    _logger.LogInformation($"Saved {_customDiscs.CustomDiscs.Count} custom discs to {_customDiscsFilePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom discs");
            }
        }

        public CustomDisc GetCustomDisc(string changerKey, string slot)
        {
            return _customDiscs.CustomDiscs.FirstOrDefault(d => d.ChangerKey == changerKey && d.Slot == slot);
        }

        public List<CustomDisc> GetCustomDiscsForChanger(string changerKey)
        {
            return _customDiscs.CustomDiscs.Where(d => d.ChangerKey == changerKey).ToList();
        }

        public async Task AddOrUpdateCustomDisc(CustomDisc customDisc)
        {
            lock (_lockObject)
            {
                // Remove existing custom disc for this slot if it exists
                _customDiscs.CustomDiscs.RemoveAll(d => d.ChangerKey == customDisc.ChangerKey && d.Slot == customDisc.Slot);
                
                // Add the new/updated custom disc
                _customDiscs.CustomDiscs.Add(customDisc);
            }

            await SaveCustomDiscs();
        }

        public async Task RemoveCustomDisc(string changerKey, string slot)
        {
            lock (_lockObject)
            {
                _customDiscs.CustomDiscs.RemoveAll(d => d.ChangerKey == changerKey && d.Slot == slot);
            }

            await SaveCustomDiscs();
        }

        public async Task DeleteCustomDisc(string changerKey, string slot)
        {
            await RemoveCustomDisc(changerKey, slot);
        }

        public Dictionary<string, CustomDisc> GetAllCustomDiscs()
        {
            var result = new Dictionary<string, CustomDisc>();
            foreach (var disc in _customDiscs.CustomDiscs)
            {
                string key = $"{disc.ChangerKey}_{disc.Slot}";
                result[key] = disc;
            }
            return result;
        }
    }
}

