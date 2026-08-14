using System.Diagnostics;

namespace Expense.Web.Services;

/// <summary>
/// Starts/stops the local clipboard-watcher background process (see
/// docs/amazon-order-scraper-bookmarklet.md) in lockstep with the Review Queue page being
/// open - the watcher never runs the rest of the time. It talks to Amazon-facing content
/// only through what it already finds sitting on the clipboard, never fetches anything
/// itself, so there's no reason to limit how long it runs beyond "while someone might
/// plausibly use it."
/// </summary>
public interface IClipboardWatcherController
{
    Task StartAsync();
    Task StopAsync();
}

public class ClipboardWatcherController : IClipboardWatcherController
{
    private const string ServiceName = "expense-clipboard-watcher.service";

    public Task StartAsync() => RunSystemctlAsync("start");
    public Task StopAsync() => RunSystemctlAsync("stop");

    // Best-effort - if systemctl isn't available or the unit isn't installed, the watcher
    // just never runs, which degrades to "paste it in yourself" - the bookmarklet and the
    // manual paste target above it keep working regardless. Never blocks or breaks the rest
    // of the page over this.
    private static async Task RunSystemctlAsync(string action)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "systemctl",
                ArgumentList = { "--user", action, ServiceName },
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            // Never let this break the page - see class remarks. Logged so a persistent
            // failure (e.g. the unit file missing) is at least visible in the journal.
            Console.Error.WriteLine($"ClipboardWatcherController: failed to {action} {ServiceName}: {ex.Message}");
        }
    }
}
