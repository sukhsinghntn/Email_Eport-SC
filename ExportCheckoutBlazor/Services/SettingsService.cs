using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace NDAProcesses.Services
{
    public class SettingsService : IDisposable
    {
        private readonly string _customFilePath;
        private readonly FileSystemWatcher _watcher;
        private readonly IConfiguration _config;

        public AppSettings Settings { get; private set; }

        public SettingsService(IConfiguration config, IWebHostEnvironment env)
        {
            _config         = config;
            _customFilePath = Path.Combine(env.ContentRootPath, "appsettings.custom.json");

            // Load defaults + any existing overrides
            ReloadAll();

            // Watch for UI saves so we pick them up immediately
            var dir = Path.GetDirectoryName(_customFilePath);
            var file = Path.GetFileName(_customFilePath);
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
            };
            _watcher.Changed += (_, __) => {
                // small debounce
                System.Threading.Thread.Sleep(100);
                ReloadAll();
            };
            _watcher.EnableRaisingEvents = true;
        }

        private void ReloadAll()
        {
            // 1) Bind built-in appsettings.json → Settings
            var defaults = new AppSettings();
            _config.GetSection("AppSettings").Bind(defaults);

            // 2) If custom file exists, overlay its values
            if (File.Exists(_customFilePath))
            {
                try
                {
                    var custom = JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(_customFilePath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    if (custom != null)
                    {
                        defaults.DaysBack      = custom.DaysBack;
                        defaults.ScheduledTime = custom.ScheduledTime;

                        if (custom.Recipients?.Any() == true)
                        {
                            // Convert List<string> → string[]
                            defaults.Recipients = custom.Recipients.ToArray();
                        }
                    }
                }
                catch
                {
                    // ignore parse errors, stick with defaults
                }
            }

            Settings = defaults;
        }


        // 1) Add this event:
        public event Action? SettingsChanged;

        public void Save()
        {
            // Persist current Settings to appsettings.custom.json…
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_customFilePath, json);

            // 2) Notify subscribers
            SettingsChanged?.Invoke();
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }

}
