using Gelato.LatAdd;
using Gelato.Services;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Gelato.Decorators.MediaSources;

/// <summary>
/// The streams found dead lately, and the stream a playback of a title's default source moved on
/// to when that one did not start.
/// </summary>
internal sealed class StreamFailover(
    MediaSourceManagerDecorator decorator,
    ILogger<MediaSourceManagerDecorator> log
)
{
    private readonly ILogger<MediaSourceManagerDecorator> _log = log;

    /// <summary>How long one stream gets to answer, and then to deliver its first bytes.</summary>
    internal static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long a playback that moved on from a dead stream keeps to its replacement.</summary>
    private static readonly TimeSpan FailoverTtl = TimeSpan.FromHours(12);

    // The stream a playback moved on to, by the dead stream's row. The source keeps the id the
    // client asked for: Jellyfin picks the source out of the answer by that id, in PlaybackInfo
    // and in every streaming request after it.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (string Row, DateTime At)
    > _failover = new();

    // The rows whose other route a playback already tried: a LAT-ADD version's relay and node
    // URLs are one file, and once both failed the next quality is next (fork §3.2).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
        _routeTried = new();

    /// <summary>
    /// At the start of a playback: the stream the title plays by default, or the next of its
    /// streams (<see cref="StreamLinks.MaxStreams"/> in all) when it is known not to start: its
    /// pre-probe or its playback's probe found it dead lately (<see cref="MarkDead"/>). Nothing is fetched for that, so a stream
    /// that works starts as fast as before. A version picked by hand (one that is not the first)
    /// and a stream row's own page play as they are. <paramref name="canMoveOn"/> says whether
    /// the playback moves on again when the probe of the stream returned finds no video; when
    /// every stream is known dead, the first plays as it did before.
    /// </summary>
    /// <remarks>
    /// The fork adds two steps ahead of the next-quality scan: a version whose URL expired has
    /// its title's streams re-synced and plays the fresh URL (fork §4.2), and a version with a
    /// route of its own (LAT-ADD's relay twin, fork §3.2) plays that before another quality.
    /// </remarks>
    internal MediaSourceInfo FailOver(
        BaseItem item,
        IReadOnlyList<MediaSourceInfo> sources,
        MediaSourceInfo selected,
        out bool canMoveOn
    )
    {
        canMoveOn = false;
        var key = PreProbes.PreProbeKey(selected);
        _failover.TryRemove(key, out _);
        if (
            item.IsStremioNonPrimary()
            || !ReferenceEquals(selected, sources[0])
            || StreamLinks.AsHttpUri(selected.Path) is null
        )
        {
            return selected;
        }

        // A signature past its expiry is not a dead stream but a stale URL: the title's rows are
        // re-synced and this playback takes the fresh one (fork §4.2).
        if (RenewExpiredUrl(selected))
            return selected;

        if (!GelatoPlugin.Instance!.Configuration.PlaybackFailover)
            return selected;

        // The version's own other route, before any other quality: the relay that did not answer
        // hands over to the node's own URL of the same file (fork §3.2).
        if (DeadRecently(key) && TryOwnRoute(key, selected))
        {
            canMoveOn = true;
            return selected;
        }

        var candidates = sources
            .Where(s => StreamLinks.AsHttpUri(s.Path) is not null)
            .Take(StreamLinks.MaxStreams)
            .ToList();
        var index = candidates.FindIndex(s => !DeadRecently(PreProbes.PreProbeKey(s)));
        if (index < 0)
        {
            _log.LogWarning(
                "None of the first {Count} streams of {Item} started lately; playing the first",
                candidates.Count,
                item.Name
            );
            return selected;
        }

        canMoveOn = true;
        if (index == 0)
            return selected;

        var candidate = candidates[index];
        _failover[key] = (PreProbes.PreProbeKey(candidate), DateTime.UtcNow);
        candidate.Id = selected.Id;
        _log.LogInformation(
            "Playing stream {Index} of {Item}: the streams before it did not start",
            index + 1,
            item.Name
        );
        return candidate;
    }

    /// <summary>
    /// The stream a playback of <paramref name="selected"/> moved on to, under the id that was
    /// asked for; <paramref name="selected"/> itself when it did not.
    /// </summary>
    internal MediaSourceInfo FailedOver(IReadOnlyList<MediaSourceInfo> list, MediaSourceInfo selected)
    {
        var key = PreProbes.PreProbeKey(selected);
        if (!_failover.TryGetValue(key, out var entry))
            return selected;

        var now = DateTime.UtcNow;
        if (now - entry.At >= FailoverTtl)
        {
            _failover.TryRemove(key, out _);
            return selected;
        }

        if (list.FirstOrDefault(s => PreProbes.PreProbeKey(s) == entry.Row) is not { } replacement)
            return selected;

        replacement.Id = selected.Id;
        return replacement;
    }

    // Sources found dead lately: no answer, an error, a body cut short or no video at all. A
    // playback of the title's default source moves on from them (FailOver).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _dead =
        new();

    internal void MarkDead(string key)
    {
        // A signature past its expiry answers 401/403, which is a stale URL rather than a dead
        // stream: the title's rows are re-synced for a fresh one instead of the stream being
        // set aside (fork §4.2).
        if (ExpiredRow(key))
        {
            _log.LogInformation(
                "A stream's signed URL expired; re-syncing its title's streams instead of setting the stream aside"
            );
            ResyncRowInBackground(key);
            return;
        }

        decorator.PreProbes.MarkPreProbeFailed(key);
        foreach (var (staleKey, deadAt) in _dead)
        {
            if (DateTime.UtcNow - deadAt >= PreProbes.PreProbeRetry)
                _dead.TryRemove(staleKey, out _);
        }

        _dead[key] = DateTime.UtcNow;
    }

    private bool DeadRecently(string key) =>
        _dead.TryGetValue(key, out var deadAt) && DateTime.UtcNow - deadAt < PreProbes.PreProbeRetry;

    /// <summary>
    /// The version's other route, when there is one and it was not tried lately: the source keeps
    /// its id and plays the other URL of the same file.
    /// </summary>
    private bool TryOwnRoute(string key, MediaSourceInfo selected)
    {
        if (
            _routeTried.TryGetValue(key, out var at)
            && DateTime.UtcNow - at < PreProbes.PreProbeRetry
        )
            return false;

        var route = RowOf(selected)?.GelatoData<string?>("fallbackPath");
        if (string.IsNullOrWhiteSpace(route) || StreamLinks.AsHttpUri(route) is null)
            return false;

        // A fallback that is itself expired renews instead: the fresh sync decides the route.
        if (StreamKeys.Expired(route))
            return RenewExpiredUrl(selected);

        _routeTried[key] = DateTime.UtcNow;
        selected.Path = route;
        _log.LogInformation("Playing the version's own alternate route: its primary did not start");
        return true;
    }

    /// <summary>
    /// Whether the source's row serves a URL past its expiry: a stale signature to renew, not a
    /// stream to give up on.
    /// </summary>
    private bool ExpiredRow(string key) =>
        Guid.TryParse(key, out var rowId)
        && decorator.Library.GetItemById(rowId) is { } row
        && (StreamKeys.Expired(row.Path) || StreamKeys.Expired(row.GelatoData<string?>("fallbackPath")));

    /// <summary>
    /// Re-syncs the title's rows for a fresh URL and, when one came, plays it: the source keeps
    /// its id and takes the renewed path. True when the source changed.
    /// </summary>
    private bool RenewExpiredUrl(MediaSourceInfo selected)
    {
        if (RowOf(selected) is not { } row || !StreamKeys.Expired(row.Path))
            return false;

        ResyncRow(row);

        var fresh = decorator.Library.GetItemById(row.Id);
        if (fresh?.Path is not { } path || path == row.Path || StreamKeys.Expired(path))
            return false;

        selected.Path = path;
        _log.LogInformation("A stream's signed URL expired; playing its re-synced URL");
        return true;
    }

    private Video? RowOf(MediaSourceInfo source) =>
        Guid.TryParse(source.ETag ?? source.Id, out var rowId)
            ? decorator.Library.GetItemById(rowId) as Video
            : null;

    private void ResyncRowInBackground(string key)
    {
        if (!Guid.TryParse(key, out var rowId))
            return;
        _ = Task.Run(() =>
        {
            try
            {
                if (decorator.Library.GetItemById(rowId) is { } row)
                    ResyncRow(row);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Re-syncing a title's streams after an expired URL failed");
            }
        });
    }

    /// <summary>
    /// Syncs the streams of the title a row belongs to, as a page open does: the addon's answer
    /// carries fresh signatures for every row.
    /// </summary>
    private void ResyncRow(BaseItem row)
    {
        var owner =
            row is Video { PrimaryVersionId: { } owner_ }
                ? decorator.Library.GetItemById(owner_)
                : row;
        if (owner is null)
            return;

        var userId =
            row.GelatoData<List<Guid>>("userIds")?.FirstOrDefault() ?? Guid.Empty;

        // As a page open does: waited for, so the playback that asked takes the fresh URL.
        decorator
            .Manager.Value.SyncStreams(owner, userId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
