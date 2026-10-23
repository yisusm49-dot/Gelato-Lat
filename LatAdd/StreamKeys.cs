using System.Globalization;

namespace Gelato.LatAdd;

/// <summary>
/// The stable identity of a stream's file, and what its signed URL says about itself (fork §4.1).
/// </summary>
public static class StreamKeys
{
    /// <summary>
    /// The file an http(s) URL serves: its path without the query string, and without the relay
    /// node prefix a relayed copy carries. The addon's URLs are signed: the signature, expiry and
    /// user id in the query change on every answer, so the full URL identifies an answer, not a
    /// file. The path is the stable part.
    /// </summary>
    /// <remarks>
    /// A direct URL is <c>/media/&lt;mediaId&gt;/&lt;basename&gt;</c>; its relayed twin carries the
    /// serving node in the path (<c>/&lt;node-host&gt;/media/&lt;mediaId&gt;/&lt;basename&gt;</c>),
    /// which is stripped so both routes of one file come out as the same key. Null for anything
    /// that is not an absolute http(s) URL.
    /// </remarks>
    public static string? StableFileKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var path = uri.AbsolutePath;
        var mediaAt = path.IndexOf("/media/", StringComparison.OrdinalIgnoreCase);
        if (mediaAt > 0)
            path = path[mediaAt..];

        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// When a signed URL expires, from its <c>exp</c> query parameter (unix seconds), or null when
    /// it has none or does not parse. Read only: the signature is never rebuilt here.
    /// </summary>
    public static DateTimeOffset? Expiry(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var exp = System.Web.HttpUtility.ParseQueryString(uri.Query)["exp"];
        return long.TryParse(exp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>
    /// Whether a signed URL is past (or within a minute of) its expiry: its answer is 401/403,
    /// which is not a dead stream but a stale one - the title's streams need a new sync (fork §4.2).
    /// </summary>
    public static bool Expired(string? url) =>
        Expiry(url) is { } expiry && DateTimeOffset.UtcNow >= expiry - TimeSpan.FromMinutes(1);

    /// <summary>Guids for stream keys, MD5 of the key's bytes, as Gelato always built them.</summary>
    public static Guid KeyGuid(string key)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
