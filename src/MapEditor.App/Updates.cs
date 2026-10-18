using System.Net.Http;
using Velopack;

namespace MapEditor.App;

/// <summary>
/// Update checks through Velopack. They only run for an installed copy (not a build run from source) and only
/// when an update feed is set: <see cref="AppSettings.UpdateUrl"/>, or the feed baked in at release time.
/// </summary>
public static class Updates
{
    /// <summary>The release feed (a GitHub releases page or a folder of Velopack releases), set by the release build.</summary>
    public const string? BuiltInFeed = null;

    public static string? Feed(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.UpdateUrl) ? BuiltInFeed : settings.UpdateUrl;

    public sealed record Result(string Message, UpdateManager? Manager = null, UpdateInfo? Update = null);

    public static async Task<Result> CheckAsync(AppSettings settings)
    {
        if (Feed(settings) is not { } feed)
            return new("No update feed is set. Download new versions from the release page.");
        var manager = new UpdateManager(feed);
        if (!manager.IsInstalled)
            return new("Updates only work for the installed editor, not a copy run from a build folder.");
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            return update is null
                ? new($"You have the latest version ({manager.CurrentVersion}).")
                : new($"Version {update.TargetFullRelease.Version} is available (you have {manager.CurrentVersion}).", manager, update);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            return new("Could not check for updates: " + ex.Message);
        }
    }

    /// <summary>Downloads the update, then restarts into it. Unsaved work must be saved first.</summary>
    public static async Task ApplyAsync(Result result)
    {
        if (result is not { Manager: { } manager, Update: { } update }) return;
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
