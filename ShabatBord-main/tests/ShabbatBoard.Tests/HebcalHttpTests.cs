using System.Net;
using Microsoft.Extensions.Caching.Memory;
using ShabbatBoard.Application;
using ShabbatBoard.Infrastructure;

namespace ShabbatBoard.Tests;

public class HebcalHttpTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task RequestsIsraeliScheduleAndCachesSuccessfulYear()
    {
        using var handler = new Handler(request =>
        {
            var query = request.RequestUri!.Query;
            Assert.Contains("i=on", query);
            Assert.Contains("mvch=on", query);
            Assert.Contains("start=2026-12-25", query);
            Assert.Contains("end=2029-01-14", query);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "israel-cycle-2027.json")))
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new("https://www.hebcal.com/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var provider = new HebcalCalendarProvider(client, cache);
        var cycle = await provider.GetYearAsync(2027, default);
        Assert.Equal(50, cycle.Count);
        Assert.Equal("פרשת בראשית", cycle[0].Reading);
        await provider.GetYearAsync(2027, default);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, """{"items":[]}""")]
    public async Task SurfacesUpstreamFailuresAndDoesNotCacheThem(HttpStatusCode status, string json)
    {
        using var handler = new Handler(_ => new(status) { Content = new StringContent(json) });
        using var client = new HttpClient(handler) { BaseAddress = new("https://www.hebcal.com/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var provider = new HebcalCalendarProvider(client, cache);
        await Assert.ThrowsAsync<UpstreamException>(() => provider.GetYearAsync(2027, default));
        await Assert.ThrowsAsync<UpstreamException>(() => provider.GetYearAsync(2027, default));
        Assert.Equal(2, handler.Requests);
    }
}
