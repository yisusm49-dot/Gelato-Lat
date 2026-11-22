using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gelato.Tests;

/// <summary>
/// A stream request ends when the caller's token does, also while the addon is rate-limiting or
/// silent: ResyncRow's timeout must reach the HTTP call (#15). Fake handler, no server.
/// </summary>
public sealed class ProviderCancellationFacts
{
    private delegate Task<HttpResponseMessage> Respond(
        HttpRequestMessage request,
        CancellationToken ct
    );

    private sealed class FakeHandler(Respond respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => respond(request, cancellationToken);
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static GelatoStremioProvider NewProvider(Respond streams)
    {
        var handler = new FakeHandler(
            (request, ct) =>
            {
                // No manifest: the provider goes on without one, as it does after a failed read.
                if (request.RequestUri!.AbsolutePath.EndsWith("/manifest.json", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                return streams(request, ct);
            }
        );
        return new GelatoStremioProvider(
            "https://addon.test/config",
            new FakeFactory(handler),
            NullLogger<GelatoStremioProvider>.Instance
        );
    }

    private static StremioUri Movie() => new(StremioMediaType.Movie, "tt0000001");

    [Fact]
    public async Task RetryAfter_AnHourLong_IsEndedByTheCallersToken()
    {
        var provider = NewProvider(
            (request, ct) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return Task.FromResult(resp);
            }
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetStreamsAsync(Movie(), cts.Token)
        );

        // Neither the 3600 s asked for nor the 30 s cap is waited: the token ends the wait.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"waited {watch.Elapsed}");
    }

    [Fact]
    public async Task CancellationDuringSend_PropagatesOperationCanceled()
    {
        var provider = NewProvider(
            async (request, ct) =>
            {
                // The addon never answers: only the caller's token can end the request.
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetStreamsAsync(Movie(), cts.Token)
        );
    }

    [Theory]
    [InlineData(3600, 1, 30)]
    [InlineData(2, 1, 2)]
    public void RetryAfter_IsCappedAt30Seconds(int retryAfterSec, int attempt, int expectedSec) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSec),
            GelatoStremioProvider.RetryDelay(TimeSpan.FromSeconds(retryAfterSec), attempt)
        );

    [Fact]
    public void NoRetryAfter_BacksOffByAttempt() =>
        Assert.Equal(TimeSpan.FromMilliseconds(1000), GelatoStremioProvider.RetryDelay(null, 2));
}
