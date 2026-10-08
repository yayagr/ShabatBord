using ShabbatBoard.Application;
using ShabbatBoard.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShabbatBoard.Infrastructure;

public sealed class JsonAssignmentStore : IAssignmentStore, IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public JsonAssignmentStore(string path) => this.path = Path.GetFullPath(path);

    public async Task<IReadOnlyList<Assignment>> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await LoadAsync(cancellationToken)).ToArray(); }
        finally { gate.Release(); }
    }

    public Task<Assignment> ClaimAsync(DateOnly date, string? name, CancellationToken cancellationToken) =>
        MutateAsync(date, name, null, false, cancellationToken);

    public Task<Assignment> EditAsync(DateOnly date, string? name, Guid version, CancellationToken cancellationToken) =>
        MutateAsync(date, name, version, false, cancellationToken);

    public async Task RemoveAsync(DateOnly date, Guid version, CancellationToken cancellationToken) =>
        await MutateAsync(date, null, version, true, cancellationToken);

    private async Task<Assignment> MutateAsync(
        DateOnly date, string? name, Guid? expectedVersion, bool remove, CancellationToken cancellationToken)
    {
        CalendarRules.ValidateDate(date);
        var replacement = remove ? null : Assignment.Create(date, name);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var assignments = await LoadAsync(cancellationToken);
            var existing = assignments.SingleOrDefault(a => a.Date == date);
            if (expectedVersion is null && existing is not null)
                throw new RuleException("השבת כבר נבחרה על ידי משפחה אחרת. יש לרענן את הלוח.", RuleFailure.Conflict);
            if (expectedVersion is not null)
            {
                if (existing is null)
                    throw new RuleException("השיבוץ אינו קיים עוד. יש לרענן את הלוח.", RuleFailure.NotFound);
                if (existing.Version != expectedVersion)
                    throw new RuleException("השיבוץ השתנה מאז פתיחת החלונית. יש לרענן את הלוח.", RuleFailure.Conflict);
            }
            assignments.RemoveAll(a => a.Date == date);
            if (replacement is not null)
                assignments.Add(replacement);
            await SaveAsync(assignments, cancellationToken);
            return replacement ?? existing!;
        }
        finally { gate.Release(); }
    }

    private async Task<List<Assignment>> LoadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        StoreDocument document;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            document = await JsonSerializer.DeserializeAsync<StoreDocument>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Assignment store cannot be null.");
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Assignment store contains invalid JSON.", ex);
        }

        if (document.SchemaVersion != 1 || document.Assignments is null)
            throw new InvalidDataException("Assignment store schema is unsupported.");
        var dates = new HashSet<DateOnly>();
        foreach (var assignment in document.Assignments)
        {
            if (assignment is null || !dates.Add(assignment.Date) || assignment.Version == Guid.Empty)
                throw new InvalidDataException("Assignment store contains invalid or duplicate assignments.");
            try
            {
                var validated = Assignment.Create(assignment.Date, assignment.FamilyName);
                if (validated.FamilyName != assignment.FamilyName)
                    throw new InvalidDataException("Assignment store contains a non-normalized name.");
            }
            catch (RuleException ex)
            {
                throw new InvalidDataException("Assignment store violates assignment rules.", ex);
            }
        }
        return document.Assignments;
    }

    private async Task SaveAsync(List<Assignment> assignments, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new StoreDocument(1, assignments.OrderBy(a => a.Date).ToList()), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private sealed record StoreDocument(int SchemaVersion, List<Assignment>? Assignments);
    public void Dispose() => gate.Dispose();
}
