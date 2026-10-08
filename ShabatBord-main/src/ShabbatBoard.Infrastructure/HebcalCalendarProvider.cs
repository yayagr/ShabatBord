using Microsoft.Extensions.Caching.Memory;
using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShabbatBoard.Infrastructure;

public sealed class HebcalCalendarProvider(HttpClient client, IMemoryCache cache) : ICalendarProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<Shabbat>> GetYearAsync(int year, CancellationToken cancellationToken)
    {
        CalendarRules.ValidateYear(year);
        var key = $"hebcal-israel-bereshit-cycle-{year}";
        if (cache.TryGetValue<IReadOnlyList<Shabbat>>(key, out var cached))
            return cached!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<IReadOnlyList<Shabbat>>(key, out cached))
                return cached!;
            var start = new DateOnly(year, 1, 1).AddDays(-7);
            var end = new DateOnly(year + 2, 1, 14);
            var query = $"hebcal?v=1&cfg=json&start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}"
                + "&i=on&s=on&mvch=on&nx=on&maj=on&d=on&hdp=1&leyning=off";
            HebcalResponse response;
            try
            {
                response = await client.GetFromJsonAsync<HebcalResponse>(query, cancellationToken)
                    ?? throw new UpstreamException("Hebcal returned an empty response.");
            }
            catch (HttpRequestException ex)
            {
                throw new UpstreamException("Hebcal could not be reached.", ex);
            }
            catch (JsonException ex)
            {
                throw new UpstreamException("Hebcal returned invalid JSON.", ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpstreamException("Hebcal request timed out.", ex);
            }
            var rows = CalendarProjector.ProjectCycle(year, response);
            cache.Set(key, rows, TimeSpan.FromHours(6));
            return rows;
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
