using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Gelato.LatAdd;

/// <summary>
/// LAT-ADD's stream rows as one version per quality (fork §3.1, §3.2).
/// </summary>
/// <remarks>
/// LAT-ADD answers one row per quality tier, plus a relayed twin (name <c>⚡ …</c>) right under
/// the ones its relay forwards; the 4K row is never relayed. The twins share the file's path:
/// grouping by <see cref="StreamKeys.StableFileKey"/> folds them into one version whose primary
/// URL is the relay's and whose fallback is the node's own, or the other way round. A row whose
/// name starts with <c>⛔</c> is a clip about a missing 4K permission, not the film: dropped.
/// Rows of other addons pass through untouched.
/// </remarks>
public static partial class LatAddStreams
{
    private const string AddonMark = "LAT-ADD";

    /// <summary>Whether the list has any LAT-ADD row: only then is it reshaped.</summary>
    public static bool AnyLatAdd(IEnumerable<StremioStream> streams) =>
        streams.Any(s => IsLatAdd(s.Name));

    public static bool IsLatAdd(string? name) =>
        name?.Contains(AddonMark, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The clip a 4K the account may not watch answers with: never a version.</summary>
    public static bool IsDeniedClip(StremioStream stream) =>
        stream.Name?.TrimStart().StartsWith('\u26D4') == true;

    /// <summary>A relayed twin, told by the addon's semantics (the ⚡ name), never by its host.</summary>
    public static bool IsRelay(StremioStream stream) =>
        stream.Name?.TrimStart().StartsWith('\u26A1') == true;

    /// <summary>
    /// One stream per quality: each group of twins becomes its primary row (the relay's when
    /// <paramref name="preferRelay"/> and there is one, else the node's own) with the other
    /// route's URL as <see cref="StremioStream.FallbackUrl"/>. Groups keep coming out ordered by
    /// quality, best first, ties and unknowns in the addon's own order.
    /// </summary>
    public static List<StremioStream> GroupVersions(
        IReadOnlyList<StremioStream> streams,
        bool preferRelay,
        ILogger? log = null
    )
    {
        if (!AnyLatAdd(streams))
            return streams.ToList();

        var kept = streams.Where(s => !IsDeniedClip(s)).ToList();
        var dropped = streams.Count - kept.Count;
        if (dropped > 0)
            log?.LogInformation("LAT-ADD: dropped {Count} permission-clip row(s)", dropped);

        // The first group a file's rows fold into, in first-seen order.
        var groups = new LinkedStringMap<List<StremioStream>>();
        var withoutKey = new List<StremioStream>();
        foreach (var stream in kept)
        {
            if (StreamKeys.StableFileKey(stream.Url) is not { } key)
            {
                withoutKey.Add(stream);
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = new List<StremioStream>();
            group.Add(stream);
        }

        var merged = new List<(int Rank, StremioStream Stream)>();
        foreach (var group in groups.Values)
        {
            var relay = group.FirstOrDefault(IsRelay);
            var direct = group.FirstOrDefault(s => !IsRelay(s));

            StremioStream primary;
            if (preferRelay && relay is not null)
            {
                primary = relay;
                primary.FallbackUrl = direct?.Url;
            }
            else if (direct is not null)
            {
                primary = direct;
                primary.FallbackUrl = relay?.Url;
            }
            else
            {
                // A relay row without its twin: kept as it came.
                primary = relay!;
                primary.FallbackUrl = null;
            }

            primary.Name = VersionName(primary);
            if (primary.BehaviorHints?.BingeGroup is { } group_)
                primary.BehaviorHints.BingeGroup = NormalizeBingeGroup(group_);

            merged.Add((QualityRank(primary), primary));
        }

        // Best quality first; the addon's order breaks ties so 4K stays ahead of 1080p as sent.
        return merged
            .Select((entry, i) => (entry.Rank, Order: i, entry.Stream))
            .OrderByDescending(m => m.Rank)
            .ThenBy(m => m.Order)
            .Select(m => m.Stream)
            .Concat(withoutKey)
            .ToList();
    }

    /// <summary>
    /// The version's name, one line and no emoji: the quality (the name's second line) with the
    /// title's details (<c> | </c>-separated, the quality not repeated) and the <c>💾</c> size,
    /// joined by <c> · </c>. Example: <c>1080p · WEB-DL · Latino · 6.0 GB</c>.
    /// </summary>
    public static string VersionName(StremioStream stream)
    {
        var quality = SecondLine(stream.Name) ?? stream.Quality?.Trim();
        quality = Clean(quality);

        var size = (stream.Title ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("\U0001F4BE", StringComparison.Ordinal)); // 💾

        var details = SecondLine(stream.Title)?
            .Split('|')
            .Select(part => Clean(part))
            .Where(part =>
                !string.IsNullOrEmpty(part)
                && !string.Equals(part, quality, StringComparison.OrdinalIgnoreCase)
                && !part.StartsWith('\u26A1') // the twin's alternate-route note
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(quality))
            parts.Add(quality);
        parts.AddRange(details ?? []);
        if (!string.IsNullOrEmpty(size))
            parts.Add(Clean(size));

        var name = string.Join(" · ", parts);
        return name.Length > 0 ? name : stream.GetName();
    }

    private static string? SecondLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var lines = value.Split('\n');
        return lines.Length > 1 ? lines[1].Trim() : null;
    }

    /// <summary>One line, no emoji, no double spaces, trimmed.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var cleaned = Emojis().Replace(value.Replace('\n', ' '), "");
        while (cleaned.Contains("  ", StringComparison.Ordinal))
            cleaned = cleaned.Replace("  ", " ");
        return cleaned.Trim(' ', '·', '\uFE0F');
    }

    // Emoji and symbol blocks: misc. symbols and dingbats (⚡ ⛔), the emoji ranges, variation
    // selectors and joiners. Titles and details are plain text with accents, none of it here.
    [System.Text.RegularExpressions.GeneratedRegex(
        @"[\u2600-\u27BF\u2B00-\u2BFF\uFE00-\uFE0F\u200D]|[\uD800-\uDBFF][\uDC00-\uDFFF]"
    )]
    private static partial System.Text.RegularExpressions.Regex Emojis();

    /// <summary>
    /// The quality of a row's name, as a rank: 4K first, then 1080p, 720p, the rest in the
    /// addon's order. Numbers other than the known tiers (480p…) keep their pixel count.
    /// </summary>
    public static int QualityRank(StremioStream stream)
    {
        var quality = (SecondLine(stream.Name) ?? stream.Quality ?? "")
            .Trim()
            .ToUpperInvariant();

        return quality switch
        {
            "" => 0,
            "4K" or "2160P" or "UHD" => 5000,
            "1080P" or "FULLHD" or "FHD" => 4000,
            "720P" or "HD" => 3000,
            "480P" or "SD" => 2000,
            _ when int.TryParse(
                quality.TrimEnd('P', 'K'),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var pixels
            ) => pixels,
            _ => 0,
        };
    }

    /// <summary>
    /// A binge group without its route suffix, so the next episode follows the group whichever
    /// route its row plays (<c>lat-add-…-relay</c> → <c>lat-add-…</c>).
    /// </summary>
    public static string? NormalizeBingeGroup(string? bingeGroup)
    {
        if (string.IsNullOrEmpty(bingeGroup))
            return bingeGroup;

        const string suffix = "-relay";
        return bingeGroup.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? bingeGroup[..^suffix.Length]
            : bingeGroup;
    }

    /// <summary>
    /// The order the addon sent the rows in, kept: first-seen groups stay ahead of later ones.
    /// (Only used to preserve first-seen order while grouping; the BCL has no such map.)
    /// </summary>
    private sealed class LinkedStringMap<TValue> : IEnumerable<KeyValuePair<string, TValue>>
    {
        private readonly Dictionary<string, TValue> _map = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new();

        public TValue this[string key]
        {
            get => _map[key];
            set
            {
                if (!_map.ContainsKey(key))
                    _order.Add(key);
                _map[key] = value;
            }
        }

        public bool TryGetValue(string key, out TValue value) => _map.TryGetValue(key, out value!);

        public IEnumerable<TValue> Values => _order.Select(key => _map[key]);

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() =>
            _order.Select(key => new KeyValuePair<string, TValue>(key, _map[key])).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
