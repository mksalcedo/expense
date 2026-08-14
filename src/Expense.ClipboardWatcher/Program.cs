using System.Diagnostics;
using System.Runtime.InteropServices;

// Watches the clipboard for the Amazon order-scraper bookmarklet's JSON output (see
// docs/amazon-order-scraper-bookmarklet.md) and hands it off to the running web app, which
// does all the real work (parsing, finding the matching NeedsReview item, staging it for
// Accept/Cancel on the Review Queue page). Deliberately minimal and dependency-free - no
// database access, no domain logic, just clipboard-in, HTTP-out. Only runs while the Review
// Queue page is open (started/stopped by ReviewQueue.razor via systemctl), never scheduled,
// never talks to Amazon at all - it only ever reads what's already sitting on this machine's
// own clipboard.

var targetUrl = Environment.GetEnvironmentVariable("EXPENSE_WEB_URL") ?? "http://127.0.0.1:5266/internal/scraped-order-data";
using var httpClient = new HttpClient();

using var cts = new CancellationTokenSource();
PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    Console.WriteLine("Received SIGTERM, shutting down.");
    cts.Cancel();
    context.Cancel = true;
});

Console.WriteLine($"Clipboard watcher started - posting to {targetUrl}");

string? lastSeen = null;
while (!cts.Token.IsCancellationRequested)
{
    try
    {
        var current = ReadClipboardText();
        if (current is not null && current != lastSeen)
        {
            lastSeen = current;
            var trimmed = current.Trim();
            if (trimmed.StartsWith('{'))
            {
                await SubmitAsync(trimmed);
            }
        }
    }
    catch (Exception ex)
    {
        // A single failed poll (clipboard temporarily unreadable, network hiccup, etc.)
        // shouldn't stop the loop - just try again next tick.
        Console.Error.WriteLine($"Poll failed: {ex.Message}");
    }

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

Console.WriteLine("Clipboard watcher stopped.");
return;

// xclip exits non-zero when the clipboard holds non-text content (e.g. an image) or is
// empty - both are normal, not errors, so they're treated the same as "nothing new."
static string? ReadClipboardText()
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "xclip",
        ArgumentList = { "-selection", "clipboard", "-o" },
        RedirectStandardOutput = true,
        RedirectStandardError = true
    });
    if (process is null)
    {
        return null;
    }

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0 ? output : null;
}

async Task SubmitAsync(string json)
{
    try
    {
        using var content = new StringContent(json);
        using var response = await httpClient.PostAsync(targetUrl, content, cts.Token);
        Console.WriteLine(response.IsSuccessStatusCode
            ? "Staged a scrape for the app to show."
            : $"App didn't recognize this as order data ({(int)response.StatusCode}) - not necessarily an error, could just be an unrelated clipboard copy.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to reach the app at {targetUrl}: {ex.Message}");
    }
}
