using Gelato.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Gelato.ScheduledTasks;

public sealed class GelatoCatalogItemsSyncTask(
    ILogger<GelatoCatalogItemsSyncTask> log,
    CatalogImportService importService
) : IScheduledTask
{
    public string Name => "Import Catalogs";
    public string Key => "GelatoCatalogItemsSync";
    public string Description => "Imports items from enabled Stremio catalogs into Jellyfin.";
    public string Category => "Gelato";

    // Daily, off-peak: new titles uploaded to the addon show up without a manual run.
    // Runs after the first one only process what changed, so a daily run is cheap.
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
            },
        ];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        log.LogInformation("Starting Gelato catalog sync task...");
        await importService.SyncAllEnabledAsync(ct, progress).ConfigureAwait(false);
        log.LogInformation("Gelato catalog sync task finished.");
    }
}
