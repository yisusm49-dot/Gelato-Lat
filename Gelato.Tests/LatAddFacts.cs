using System.Text.Json;
using Gelato;
using Gelato.LatAdd;
using Xunit;

namespace Gelato.Tests;

/// <summary>
/// The fork's acceptance rules, as far as they can be checked without a server: the manifest's
/// short-form resources (§2), the stable file identity of a signed URL (§4.1) and the version
/// grouping of LAT-ADD's rows (§3.1, §3.2).
/// </summary>
public sealed class ManifestFacts
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // The manifest as LAT-ADD serves it: "catalog" as a bare string, meta and stream as objects.
    private const string LatAddManifest = """
        {
          "id": "lat-add.lzid.me",
          "name": "LAT-ADD",
          "version": "0.26.3",
          "resources": [
            "catalog",
            { "name": "meta",   "types": ["movie","series"], "idPrefixes": ["ngmovie_","ngseries_","tt","tmdb:"] },
            { "name": "stream", "types": ["movie","series"], "idPrefixes": ["ngmovie_","ngseries_","ngepisode_","tt","tmdb:"] }
          ],
          "types": ["movie","series"],
          "catalogs": [
            { "id": "lat-add_movies", "type": "movie", "name": "Movies", "extra": [ { "name": "skip", "isRequired": false }, { "name": "search", "isRequired": false } ] },
            { "id": "lat-add_series", "type": "series", "name": "Series", "extra": [ { "name": "skip", "isRequired": false }, { "name": "search", "isRequired": false } ] }
          ]
        }
        """;

    [Fact]
    public void ShortFormResource_ParsesInsteadOfThrowing()
    {
        var manifest = JsonSerializer.Deserialize<StremioManifest>(LatAddManifest, JsonOpts);

        Assert.NotNull(manifest);
        Assert.Equal(3, manifest.Resources.Count);
        Assert.Equal("catalog", manifest.Resources[0].Name);
        Assert.Equal("meta", manifest.Resources[1].Name);
        Assert.Equal("stream", manifest.Resources[2].Name);
    }

    [Fact]
    public void ShortFormResource_InheritsTheManifestTypes()
    {
        var manifest = JsonSerializer.Deserialize<StremioManifest>(LatAddManifest, JsonOpts)!;
        StremioResourceConverter.InheritManifestDefaults(manifest);

        Assert.Equal(["movie", "series"], manifest.Resources[0].Types);
        // A resource that declares its own keeps them: meta's prefixes are not the root's.
        Assert.Equal("tt", manifest.Resources[1].IdPrefixes[2]);
        Assert.Empty(manifest.Resources[1].IdPrefixes.Where(p => p == "movie"));
    }

    [Fact]
    public void ObjectFormResources_StillParse()
    {
        var manifest = JsonSerializer.Deserialize<StremioManifest>(
            """
            {
              "id": "com.aiostreams.viren070",
              "name": "AIOStreams",
              "resources": [ { "name": "catalog", "types": ["movie","series"], "idPrefixes": ["tt"] } ],
              "types": ["movie","series"],
              "catalogs": []
            }
            """,
            JsonOpts
        );

        Assert.NotNull(manifest);
        var catalog = Assert.Single(manifest.Resources);
        Assert.Equal("catalog", catalog.Name);
        Assert.Equal(["tt"], catalog.IdPrefixes);
    }

    [Fact]
    public void Catalogs_AreReadAsBefore()
    {
        var manifest = JsonSerializer.Deserialize<StremioManifest>(LatAddManifest, JsonOpts)!;

        Assert.Equal(2, manifest.Catalogs.Count);
        Assert.True(manifest.Catalogs[0].IsSearchCapable());
        Assert.True(manifest.Catalogs[0].IsImportable());
    }
}

public sealed class StreamKeyFacts
{
    private const string Direct =
        "https://node5.lat-add.com/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=AAAA&exp=1790000000&uid=7";

    private const string RelayTwin =
        "https://relay1.lat-add.com/node5.lat-add.com/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=BBBB&exp=1790000100&uid=7";

    private const string OtherFile =
        "https://node4.lat-add.com/media/ngmovfile_HupSgoQvrPJe9KjV/video.mkv?sig=CCCC&exp=1790000000&uid=7";

    [Fact]
    public void DirectAndRelayTwins_ShareTheFileKey()
    {
        Assert.Equal(
            StreamKeys.StableFileKey(Direct),
            StreamKeys.StableFileKey(RelayTwin)
        );
        Assert.Equal("/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv", StreamKeys.StableFileKey(Direct));
    }

    [Fact]
    public void AnotherFile_HasAnotherKey()
    {
        Assert.NotEqual(StreamKeys.StableFileKey(Direct), StreamKeys.StableFileKey(OtherFile));
    }

    [Fact]
    public void NonHttpUrls_HaveNoFileKey()
    {
        Assert.Null(StreamKeys.StableFileKey(null));
        Assert.Null(StreamKeys.StableFileKey("magnet:?xt=urn:btih:0123456789"));
        Assert.Null(StreamKeys.StableFileKey("/relative/path/video.mkv"));
    }

    [Fact]
    public void SignedTwins_ShareTheStreamGuid_AcrossSyncs()
    {
        // One sync's answer, the next sync's answer: the signature and expiry differ each time
        // (fork §4.1, acceptance #6).
        var first = new StremioStream { Url = Direct };
        var second = new StremioStream { Url = RelayTwin };
        var third = new StremioStream
        {
            Url = "https://node5.lat-add.com/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=DDDD&exp=1790099999&uid=7",
        };

        Assert.Equal(first.GetGuid(), second.GetGuid());
        Assert.Equal(first.GetGuid(), third.GetGuid());
        Assert.NotEqual(first.GetGuid(), new StremioStream { Url = OtherFile }.GetGuid());
    }

    [Fact]
    public void ForeignUrls_KeepTheirQueryInTheGuid()
    {
        // An addon that names its file in the query: the same path is two different files, and
        // the stable key must not fold them into one version (issue #3).
        var a = new StremioStream { Url = "https://host.example/play?file=pelicula-1080p.mkv" };
        var b = new StremioStream { Url = "https://host.example/play?file=pelicula-4k.mkv" };

        Assert.NotEqual(a.GetGuid(), b.GetGuid());
    }

    [Fact]
    public void Expiry_ReadsTheExpParameter()
    {
        var past = $"https://node4.lat-add.com/media/x/video.mkv?exp={DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()}";
        var future = $"https://node4.lat-add.com/media/x/video.mkv?exp={DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds()}";

        Assert.True(StreamKeys.Expired(past));
        Assert.False(StreamKeys.Expired(future));
        Assert.False(StreamKeys.Expired("https://node4.lat-add.com/media/x/video.mkv"));
        Assert.Null(StreamKeys.Expiry("https://node4.lat-add.com/media/x/video.mkv"));
    }
}

public sealed class VersionGroupingFacts
{
    private static StremioStream Row(
        string name,
        string title,
        string url,
        string? bingeGroup = null
    ) =>
        new()
        {
            Name = name,
            Title = title,
            Url = url,
            BehaviorHints = bingeGroup is null ? null : new StremioBehaviorHints { BingeGroup = bingeGroup },
        };

    // Akira as the addon answers it: a 4K row (never relayed), a 1080p row, its relayed twin
    // underneath, and the permission clip of a 4K the account may not watch.
    private static List<StremioStream> AkiraAnswer() =>
    [
        Row(
            "⛔ LAT-ADD\n4K",
            "Akira (1988)\n4K sin permiso para esta cuenta",
            "https://node4.lat-add.com/media/ngmovfile_denied/clip.mp4?sig=X&exp=1790000000&uid=7",
            "lat-add-local-directplay-denied"
        ),
        Row(
            "🔀 LAT-ADD\n4K",
            "Akira (1988)\n4K | BluRay | Dolby Vision | HDR10+ | Dolby Atmos | H.265 | Latino\n💾 58 GB",
            "https://node4.lat-add.com/media/ngmovfile_HupSgoQvrPJe9KjV/video.mkv?sig=A&exp=1790000000&uid=7",
            "lat-add-local-directplay-4k"
        ),
        Row(
            "🔀 LAT-ADD\n1080p",
            "Akira (1988)\n1080p | WEB-DL | H.264 | Latino\n💾 6.0 GB",
            "https://tiger.lzid.me:8443/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=B&exp=1790000000&uid=7",
            "lat-add-local-directplay-1080"
        ),
        Row(
            "⚡ LAT-ADD\n1080p",
            "Akira (1988)\n1080p | WEB-DL | H.264 | Latino\n💾 6.0 GB\n⚡ Ruta alterna: úsala si el directo se traba",
            "https://relay1.lat-add.com/tiger.lzid.me:8443/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=C&exp=1790000000&uid=7",
            "lat-add-local-directplay-1080-relay"
        ),
    ];

    [Fact]
    public void OneVersionPerQuality_ClipDropped()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);

        Assert.Equal(2, versions.Count);
        Assert.DoesNotContain(versions, v => v.Url.Contains("denied"));
    }

    [Fact]
    public void Names_AreOneLine_AndHaveNoEmoji()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);

        Assert.Equal("4K · BluRay · Dolby Vision · HDR10+ · Dolby Atmos · H.265 · Latino · 58 GB", versions[0].Name);
        Assert.Equal("1080p · WEB-DL · H.264 · Latino · 6.0 GB", versions[1].Name);
        Assert.All(versions, v => Assert.False(v.Name!.Contains('\n')));
        Assert.All(versions, v => Assert.False(v.Name!.Contains('\u26A1') || v.Name!.Contains("\U0001F500")));
    }

    [Fact]
    public void BestQualityFirst_AddonOrderBreaksTies()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);

        Assert.StartsWith("4K", versions[0].Name);
        Assert.StartsWith("1080p", versions[1].Name);
    }

    [Fact]
    public void RelayIsPrimary_WithTheNodeAsFallback()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);
        var v1080 = versions[1];

        Assert.StartsWith("https://relay1.lat-add.com/", v1080.Url);
        Assert.Equal(
            "https://tiger.lzid.me:8443/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=B&exp=1790000000&uid=7",
            v1080.FallbackUrl
        );
        // The 4K tier is never relayed: no twin, no fallback.
        Assert.Null(versions[0].FallbackUrl);
    }

    [Fact]
    public void WithoutRelayPreference_TheNodeIsPrimary_WithRelayAsFallback()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: false);
        var v1080 = versions[1];

        Assert.StartsWith("https://tiger.lzid.me:8443/", v1080.Url);
        Assert.StartsWith("https://relay1.lat-add.com/", v1080.FallbackUrl);
    }

    [Fact]
    public void BingeGroup_LosesItsRelaySuffix()
    {
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);

        Assert.Equal("lat-add-local-directplay-1080", versions[1].BehaviorHints!.BingeGroup);
        Assert.Equal("group", LatAddStreams.NormalizeBingeGroup("group-RELAY"));
        Assert.Equal("group", LatAddStreams.NormalizeBingeGroup("group"));
    }

    [Fact]
    public void OtherAddons_PassThroughUntouched()
    {
        var rows = new List<StremioStream>
        {
            new()
            {
                Name = "Dropload\n1080p",
                Title = "Some movie",
                Url = "https://dropload.io/d/abcdef/file.mkv",
            },
            new()
            {
                Name = "SuperVideo\n720p",
                Title = "Some movie",
                Url = "https://supervideo.cc/d/uvwxyz/file.mkv",
            },
        };

        var versions = LatAddStreams.GroupVersions(rows, preferRelay: true);

        Assert.Equal(2, versions.Count);
        Assert.Equal("Dropload\n1080p", versions[0].Name);
        Assert.Null(versions[0].FallbackUrl);
    }

    [Fact]
    public void TwinsShareTheGuid_SoASyncKeepsTheVersion()
    {
        // With the twins folded into one row, the row's guid is the file's: the next sync's new
        // signature finds the same version instead of deleting and recreating it.
        var versions = LatAddStreams.GroupVersions(AkiraAnswer(), preferRelay: true);

        Assert.Equal(
            versions[1].GetGuid(),
            Row(
                    "⚡ LAT-ADD\n1080p",
                    "Akira (1988)\n1080p | WEB-DL",
                    "https://relay1.lat-add.com/tiger.lzid.me:8443/media/ngmovfile_0OiEXfUPbuSG-yOR/video.mkv?sig=NEW&exp=1795555555&uid=7"
                )
                .GetGuid()
        );
    }
}
