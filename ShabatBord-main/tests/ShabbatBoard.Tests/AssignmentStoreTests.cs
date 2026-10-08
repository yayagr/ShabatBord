using System.Text.Json;
using ShabbatBoard.Domain;
using ShabbatBoard.Infrastructure;

namespace ShabbatBoard.Tests;

public sealed class AssignmentStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shabbat-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(directory, "assignments.json");
    private JsonAssignmentStore Store => new(FilePath);
    private static readonly DateOnly Date = new(2027, 1, 2);

    [Fact]
    public async Task PersistsThroughRestartAndSupportsEditRemove()
    {
        var first = await Store.ClaimAsync(Date, " Cohen ", default);
        var read = Assert.Single(await Store.ReadAsync(default));
        Assert.Equal(first, read);
        var updated = await Store.EditAsync(Date, "Levi", first.Version, default);
        Assert.NotEqual(first.Version, updated.Version);
        Assert.Equal("Levi", Assert.Single(await Store.ReadAsync(default)).FamilyName);
        await Store.RemoveAsync(Date, updated.Version, default);
        Assert.Empty(await Store.ReadAsync(default));
    }

    [Fact]
    public async Task RejectsDuplicateClaims()
    {
        await Store.ClaimAsync(Date, "Cohen", default);
        var error = await Assert.ThrowsAsync<RuleException>(() => Store.ClaimAsync(Date, "Levi", default));
        Assert.Equal(RuleFailure.Conflict, error.Failure);
    }

    [Fact]
    public async Task RejectsStaleEditAndRemoval()
    {
        var first = await Store.ClaimAsync(Date, "Cohen", default);
        var current = await Store.EditAsync(Date, "Levi", first.Version, default);
        var edit = await Assert.ThrowsAsync<RuleException>(() => Store.EditAsync(Date, "Other", first.Version, default));
        var remove = await Assert.ThrowsAsync<RuleException>(() => Store.RemoveAsync(Date, first.Version, default));
        Assert.Equal(RuleFailure.Conflict, edit.Failure);
        Assert.Equal(RuleFailure.Conflict, remove.Failure);
        Assert.Equal(current, Assert.Single(await Store.ReadAsync(default)));
    }

    [Fact]
    public async Task RejectsMissingAssignment()
    {
        var error = await Assert.ThrowsAsync<RuleException>(() => Store.EditAsync(Date, "Levi", Guid.NewGuid(), default));
        Assert.Equal(RuleFailure.NotFound, error.Failure);
    }

    [Fact]
    public async Task SerializesConcurrentWritesWithoutLosingUpdates()
    {
        var store = Store;
        var dates = CalendarRules.Saturdays(2027);
        await Task.WhenAll(dates.Select(date => store.ClaimAsync(date, "Cohen", default)));
        Assert.Equal(52, (await store.ReadAsync(default)).Count);
    }

    [Fact]
    public async Task ConcurrentClaimsHaveExactlyOneWinner()
    {
        var store = Store;
        async Task<bool> Claim(string name)
        {
            try { await store.ClaimAsync(Date, name, default); return true; }
            catch (RuleException ex) when (ex.Failure == RuleFailure.Conflict) { return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Claim($"Family {i}")));
        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(9, results.Count(won => !won));
        Assert.Single(await store.ReadAsync(default));
    }

    [Fact]
    public async Task CancelledMutationLeavesExistingStateIntact()
    {
        var store = Store;
        var first = await store.ClaimAsync(Date, "Cohen", default);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.EditAsync(Date, "Levi", first.Version, cancellation.Token));
        Assert.Equal(first, Assert.Single(await store.ReadAsync(default)));
    }

    [Fact]
    public async Task PreservesCorruptFile()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath, "{broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync(default));
        Assert.Equal("{broken", await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task RejectsUnsupportedSchemaAndDuplicateDates()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath, """{"schemaVersion":9,"assignments":[]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync(default));
        var assignment = new Assignment(Date, "Cohen", Guid.NewGuid());
        await File.WriteAllTextAsync(FilePath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            assignments = new[] { assignment, assignment }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync(default));
    }

    [Fact]
    public async Task FailedWriteDoesNotCreateAssignment()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "blocked"), "not a directory");
        var store = new JsonAssignmentStore(Path.Combine(directory, "blocked", "assignments.json"));
        await Assert.ThrowsAnyAsync<IOException>(() => store.ClaimAsync(Date, "Cohen", default));
        Assert.Equal("not a directory", await File.ReadAllTextAsync(Path.Combine(directory, "blocked")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
