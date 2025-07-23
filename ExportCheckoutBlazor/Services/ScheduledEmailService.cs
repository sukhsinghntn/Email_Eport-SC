using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace NDAProcesses.Services
{
    public class ScheduledEmailService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly SettingsService _settings;
        private Timer? _timer;

        public ScheduledEmailService(IServiceProvider services,
                                     SettingsService settings)
        {
            _services = services;
            _settings = settings;

            // Subscribe to changes
            _settings.SettingsChanged += OnSettingsChanged;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ScheduleNextRun();
            return Task.CompletedTask;
        }

        private void OnSettingsChanged()
        {
            // Whenever the UI Save button fires, tear down and re-schedule
            _timer?.Change(Timeout.Infinite, 0);
            _timer?.Dispose();
            ScheduleNextRun();
        }

        private void ScheduleNextRun()
        {
            var now = DateTime.Now;
            var sched = _settings.Settings.ScheduledTime;
            var nextRun = now.Date
                           .AddHours(sched.Hours)
                           .AddMinutes(sched.Minutes);

            if (nextRun <= now)
                nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            // Recurring every 24h
            _timer = new Timer(async _ => await DoWork(), null,
                               delay, TimeSpan.FromDays(1));
        }

        private async Task DoWork()
        {
            using var scope = _services.CreateScope();
            var exporter = scope.ServiceProvider.GetRequiredService<ExportService>();
            var sett = _settings.Settings;
            // Convert array to list if needed:
            var toList = sett.Recipients.ToList();
            exporter.RunExport(sett.DaysBack, toList, null);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return base.StopAsync(cancellationToken);
        }
    }


}
