using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using ShabbatBoard.Infrastructure;

namespace ShabbatBoard.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shabbat-api-tests-" + Guid.NewGuid());

    private sealed class Factory(string path) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:FilePath", path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICalendarProvider>();
                services.AddSingleton<ICalendarProvider, FixtureCalendar>();
            });
        }
    }

    private sealed class FixtureCalendar : ICalendarProvider
    {
        public async Task<IReadOnlyList<Shabbat>> GetYearAsync(int year, CancellationToken cancellationToken) =>
            CalendarProjector.ProjectCycle(year, await BereshitCycleTests.Fixture(year));
    }

    [Fact]
    public async Task CalendarClaimEditConflictRemoveFlow()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        var calendar = await client.GetFromJsonAsync<CalendarYear>("/api/calendar?year=2027");
        Assert.Equal(50, calendar!.Shabbatot.Count);
        Assert.Equal("פרשת בראשית", calendar.Shabbatot[0].Reading);
        Assert.Equal(new DateOnly(2027, 10, 30), calendar.Shabbatot[0].Date);
        Assert.Equal(new DateOnly(2028, 10, 7), calendar.Shabbatot[^1].Date);
        var claim = await client.PostAsJsonAsync("/api/assignments/2027-10-30", new { familyName = "משפחת כהן" });
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);
        var first = (await claim.Content.ReadFromJsonAsync<Assignment>())!;
        var duplicate = await client.PostAsJsonAsync("/api/assignments/2027-10-30", new { familyName = "Levi" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var edit = await client.PutAsJsonAsync("/api/assignments/2027-10-30", new { familyName = "Levi", version = first.Version });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var updated = (await edit.Content.ReadFromJsonAsync<Assignment>())!;
        var stale = await client.DeleteAsync($"/api/assignments/2027-10-30?version={first.Version}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        calendar = await client.GetFromJsonAsync<CalendarYear>("/api/calendar?year=2027");
        Assert.Equal("Levi", calendar!.Shabbatot[0].Assignment!.FamilyName);
        var remove = await client.DeleteAsync($"/api/assignments/2027-10-30?version={updated.Version}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        calendar = await client.GetFromJsonAsync<CalendarYear>("/api/calendar?year=2027");
        Assert.Null(calendar!.Shabbatot[0].Assignment);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task CyclePreservesAssignmentsOnBothSidesOfJanuary()
    {
        var path = Path.Combine(directory, "assignments.json");
        using (var store = new JsonAssignmentStore(path))
        {
            await store.ClaimAsync(new(2027, 10, 30), "First", default);
            await store.ClaimAsync(new(2028, 1, 1), "Next year", default);
            await store.ClaimAsync(new(2027, 1, 2), "Previous cycle", default);
        }
        using var factory = new Factory(path);
        using var client = factory.CreateClient();
        var calendar = (await client.GetFromJsonAsync<CalendarYear>("/api/calendar?year=2027"))!;
        Assert.Equal("First", calendar.Shabbatot[0].Assignment!.FamilyName);
        Assert.Equal("Next year", calendar.Shabbatot.Single(row => row.Date == new DateOnly(2028, 1, 1)).Assignment!.FamilyName);
        Assert.DoesNotContain(calendar.Shabbatot, row => row.Date == new DateOnly(2027, 1, 2));
        using var restarted = new JsonAssignmentStore(path);
        Assert.Equal(3, (await restarted.ReadAsync(default)).Count);
    }

    [Fact]
    public async Task LastCycleSupportsNextYearClaimsThroughHttp()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/assignments/2101-10-15", new { familyName = "Cohen" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var assignment = (await response.Content.ReadFromJsonAsync<Assignment>())!;
        var calendar = (await client.GetFromJsonAsync<CalendarYear>("/api/calendar?year=2100"))!;
        Assert.Equal(assignment, calendar.Shabbatot[^1].Assignment);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/calendar?year=2101")).StatusCode);
    }

    [Theory]
    [InlineData("/api/calendar?year=1800")]
    [InlineData("/api/calendar?year=abc")]
    public async Task InvalidYearsReturnProblemDetails(string url)
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("2027-01-03", "Cohen")]
    [InlineData("2027-01-02", " ")]
    [InlineData("not-a-date", "Cohen")]
    public async Task InvalidClaimsReturnBadRequest(string date, string name)
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/assignments/{date}", new { familyName = name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiPathReturnsJsonNotHtml()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/families");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task MalformedJsonReturnsSafeProblemDetails()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        using var body = new StringContent("{invalid", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/assignments/2027-01-02", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("{invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditAndDeleteRequireCurrentVersion()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/assignments/2027-01-02", new { familyName = "Cohen" });
        var edit = await client.PutAsJsonAsync("/api/assignments/2027-01-02", new { familyName = "Levi" });
        var remove = await client.DeleteAsync("/api/assignments/2027-01-02");
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsCalendarAndAssignmentResponseSchemas()
    {
        using var factory = new Factory(Path.Combine(directory, "assignments.json"));
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("CalendarYear", out _));
        Assert.True(schemas.TryGetProperty("Assignment", out _));
        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/assignments/{date}")
            .GetProperty("post").GetProperty("responses");
        Assert.True(responses.TryGetProperty("201", out _));
        Assert.True(responses.TryGetProperty("409", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
