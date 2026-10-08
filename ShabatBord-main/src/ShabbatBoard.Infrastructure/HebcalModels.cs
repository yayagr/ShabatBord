using System.Text.Json.Serialization;

namespace ShabbatBoard.Infrastructure;

public sealed record HebcalResponse
{
    public List<HebcalEvent>? Items { get; init; }
}

public sealed record HebrewDateParts(string? Y, string? M, string? D);

public sealed record HebcalEvent
{
    public string? Title { get; init; }
    public string? Date { get; init; }
    public string? Category { get; init; }
    public string? Hebrew { get; init; }
    public string? Hdate { get; init; }
    [JsonPropertyName("heDateParts")]
    public HebrewDateParts? DateParts { get; init; }
    public bool Yomtov { get; init; }
}
