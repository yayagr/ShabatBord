using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using ShabbatBoard.Api;
using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using ShabbatBoard.Infrastructure;

var publishedRoot = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.Exists(Path.Combine(publishedRoot, "wwwroot")) ? publishedRoot : null
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("Hebcal", client =>
{
    client.BaseAddress = new Uri("https://www.hebcal.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ShabbatBoard/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});
builder.Services.AddSingleton<ICalendarProvider>(services => new HebcalCalendarProvider(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("Hebcal"),
    services.GetRequiredService<IMemoryCache>()));
builder.Services.AddSingleton<IAssignmentStore>(services =>
{
    var environment = services.GetRequiredService<IHostEnvironment>();
    var configured = builder.Configuration["Persistence:FilePath"] ?? "data/assignments.json";
    return new JsonAssignmentStore(Path.IsPathRooted(configured)
        ? configured : Path.Combine(environment.ContentRootPath, configured));
});
builder.Services.AddScoped<CalendarService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    await Results.Problem(statusCode: context.HttpContext.Response.StatusCode,
        title: "הבקשה לא הושלמה", detail: "הכתובת המבוקשת אינה קיימת או שהבקשה אינה תקינה.")
        .ExecuteAsync(context.HttpContext);
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ShabbatBoard API v1"));
}

app.MapGet("/api/calendar", async (int year, CalendarService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAsync(year, cancellationToken)))
    .WithName("GetCalendar").WithSummary("Every Shabbat from Bereshit in the selected Gregorian year until the next Bereshit, using the Israeli schedule.")
    .Produces<CalendarYear>().ProducesProblem(400).ProducesProblem(502).ProducesProblem(503);

app.MapPost("/api/assignments/{date}", async (
    string date, ClaimRequest request, IAssignmentStore store, CancellationToken cancellationToken) =>
{
    var assignment = await store.ClaimAsync(ParseDate(date), request.FamilyName, cancellationToken);
    return Results.Created($"/api/assignments/{date}", assignment);
}).WithName("ClaimShabbat").WithSummary("Claim a free Shabbat by entering a family name.")
    .Produces<Assignment>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(503);

app.MapPut("/api/assignments/{date}", async (
    string date, EditRequest request, IAssignmentStore store, CancellationToken cancellationToken) =>
{
    if (request.Version == Guid.Empty)
        throw new RuleException("יש לצרף את גרסת השיבוץ הנוכחית.");
    return Results.Ok(await store.EditAsync(ParseDate(date), request.FamilyName, request.Version, cancellationToken));
}).WithName("EditAssignment").WithSummary("Rename an assignment using its current version.")
    .Produces<Assignment>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);

app.MapDelete("/api/assignments/{date}", async (
    string date, Guid version, IAssignmentStore store, CancellationToken cancellationToken) =>
{
    if (version == Guid.Empty)
        throw new RuleException("יש לצרף את גרסת השיבוץ הנוכחית.");
    await store.RemoveAsync(ParseDate(date), version, cancellationToken);
    return Results.NoContent();
}).WithName("RemoveAssignment").WithSummary("Remove an assignment using its current version.")
    .Produces(204).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);

app.MapGet("/health", async (IAssignmentStore store, CancellationToken cancellationToken) =>
{
    await store.ReadAsync(cancellationToken);
    return Results.Ok(new { status = "healthy" });
});
app.Map("/api/{**path}", () => Results.Problem(statusCode: 404, title: "הכתובת אינה קיימת"));

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.CacheControl = context.File.Name == "index.html"
                ? "no-cache" : "public,max-age=86400";
        }
    });
    app.MapFallbackToFile("index.html");
}

try
{
    await app.Services.GetRequiredService<IAssignmentStore>().ReadAsync(CancellationToken.None);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    app.Logger.LogCritical("Persistent assignment storage could not be initialized ({ErrorType}). Startup aborted.",
        ex.GetType().Name);
    throw;
}
app.Run();

static DateOnly ParseDate(string value)
{
    if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        throw new RuleException("התאריך חייב להיות בפורמט YYYY-MM-DD.");
    return date;
}

public sealed record ClaimRequest(string? FamilyName);
public sealed record EditRequest(string? FamilyName, Guid Version);
public partial class Program;
