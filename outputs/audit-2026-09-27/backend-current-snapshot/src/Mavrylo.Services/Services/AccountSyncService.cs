using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mavrylo.Data;
using Mavrylo.Models;
using Microsoft.EntityFrameworkCore;

namespace Mavrylo.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncChange(string Kind, string Id, long BaseVersion, bool Deleted, JsonElement? Data);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SyncRequest(SyncChange[] Changes, long? Since = null);
public sealed record SyncRecord(string Kind, string Id, long Version, bool Deleted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Data);
public sealed record SyncResponse(string OwnerId, SyncRecord[] Records, SyncRecord[] Conflicts, long Cursor = 0, bool IsSnapshot = true);
public sealed class SyncException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}

public sealed class AccountSyncService(AppDbContext db, TimeProvider clock)
{
    public const int MaxRecords = 10000;
    public const int MaxBytes = 8 * 1024 * 1024;
    public async Task<SyncResponse> ExchangeAsync(string owner, string session, SyncChange[]? changes, CancellationToken ct, long? since = null)
    {
        if (since < 0) throw Invalid("since must be a nonnegative account cursor.");
        if (changes == null || changes.Length == 0) return await ReadAsync(owner, session, since, ct);
        if (changes.Length > 500) throw Invalid("At most 500 changes are allowed.");
        if (changes.Select(x => x == null ? default : (x.Kind, x.Id)).Distinct().Count() != changes.Length) throw Invalid("Duplicate record IDs.");
        foreach (var change in changes) Validate(change);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize revisions and writes across API instances, including new IDs.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {owner} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (user == null) throw new SyncException("invalid_session", "Account no longer exists.", 401);
        var family = await db.AccountSessions.FromSqlInterpolated($"SELECT * FROM account_sessions WHERE \"Id\" = {session} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (family == null || family.UserId != owner || family.RevokedAt != null || family.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
            throw new SyncException("invalid_session", "Session is no longer active.", 401);
        RequireEnrollment(user.IosEnrolledAt);

        // Only actual uploads pay for full capacity/relationship validation; idle
        // checks and downloads use the indexed, read-only path below.
        var rows = await db.AccountSyncRecords.Where(x => x.UserId == owner).Take(MaxRecords + 1).ToListAsync(ct);
        CheckCapacity(rows);
        // A future cursor indicates lost/restored history. Do not let an upload
        // advance the server past that boundary before the client recovers.
        if (since > user.SyncRevision) return Response(owner, rows, user.SyncRevision, null, []);
        var indexed = rows.ToDictionary(x => (x.Kind, x.Id));
        var conflicts = new List<SyncRecord>();
        var retries = new HashSet<(string Kind, string Id)>();
        foreach (var change in changes)
        {
            indexed.TryGetValue((change.Kind, change.Id), out var current);
            if ((current?.Version ?? 0) == change.BaseVersion) continue;
            // An incremental client's response may have been lost after commit.
            // Acknowledge only the exact immediately-following version/content.
            if (since.HasValue && current != null && current.Version > 0
                && current.Version - 1 == change.BaseVersion && SameContent(current, change))
                retries.Add((change.Kind, change.Id));
            else
                conflicts.Add(current == null ? new(change.Kind, change.Id, 0, true, null) : Record(current));
        }
        if (conflicts.Count > 0)
            return Response(owner, rows, user.SyncRevision, since, conflicts.ToArray());

        if (changes.Any(change => !retries.Contains((change.Kind, change.Id))))
            user.SyncRevision = checked(user.SyncRevision + 1);
        foreach (var change in changes)
        {
            if (retries.Contains((change.Kind, change.Id))) continue;
            if (!indexed.TryGetValue((change.Kind, change.Id), out var row))
            {
                row = new() { UserId = owner, Kind = change.Kind, Id = change.Id };
                rows.Add(row); indexed.Add((row.Kind, row.Id), row); db.AccountSyncRecords.Add(row);
            }
            row.Version++;
            row.ChangeRevision = user.SyncRevision;
            row.Deleted = change.Deleted;
            row.Data = change.Deleted ? null : change.Data!.Value.GetRawText();
        }
        CheckCapacity(rows);
        var decks = rows.Where(x => x.Kind == "deck" && !x.Deleted).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var word in rows.Where(x => x.Kind == "word" && !x.Deleted))
        {
            using var doc = JsonDocument.Parse(word.Data!);
            if (!decks.Contains(doc.RootElement.GetProperty("deck_id").GetString()!)) throw Invalid("A word must reference a live deck in this account.");
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Include acknowledgements even if a retry's original revision <= since.
        return Response(owner, rows, user.SyncRevision, since, [], changes.Select(x => (x.Kind, x.Id)).ToHashSet());
    }

    private async Task<SyncResponse> ReadAsync(string owner, string session, long? since, CancellationToken ct)
    {
        // A cursor and its records must come from the same committed snapshot;
        // otherwise a concurrent write could be skipped behind an advanced cursor.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var account = await db.Users.AsNoTracking().Where(x => x.Id == owner)
            .Select(x => new { x.SyncRevision, x.IosEnrolledAt }).SingleOrDefaultAsync(ct);
        if (account == null) throw new SyncException("invalid_session", "Account no longer exists.", 401);
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await db.AccountSessions.AsNoTracking().AnyAsync(x => x.Id == session && x.UserId == owner
            && x.RevokedAt == null && x.ExpiresAt > now, ct))
            throw new SyncException("invalid_session", "Session is no longer active.", 401);
        RequireEnrollment(account.IosEnrolledAt);
        var snapshot = !since.HasValue || since > account.SyncRevision;
        if (!snapshot && since == account.SyncRevision)
        {
            await tx.CommitAsync(ct);
            return new(owner, [], [], account.SyncRevision, false);
        }
        var query = db.AccountSyncRecords.AsNoTracking().Where(x => x.UserId == owner);
        if (!snapshot) query = query.Where(x => x.ChangeRevision > since!.Value);
        var rows = await query.OrderBy(x => x.ChangeRevision).ThenBy(x => x.Kind).ThenBy(x => x.Id)
            .Take(MaxRecords + 1).ToListAsync(ct);
        CheckCapacity(rows);
        await tx.CommitAsync(ct);
        return new(owner, rows.Select(Record).ToArray(), [], account.SyncRevision, snapshot);
    }

    private static void RequireEnrollment(DateTime? enrolledAt)
    {
        if (enrolledAt == null) throw new SyncException("ios_account_required", "Create or open your account in Owl AI on iPhone first.", 403);
    }

    private static bool SameContent(AccountSyncEntity row, SyncChange change)
    {
        if (row.Deleted != change.Deleted) return false;
        if (row.Deleted) return true;
        using var existing = JsonDocument.Parse(row.Data!);
        return JsonElement.DeepEquals(existing.RootElement, change.Data!.Value);
    }

    private static SyncResponse Response(string owner, List<AccountSyncEntity> rows, long cursor, long? since,
        SyncRecord[] conflicts, HashSet<(string Kind, string Id)>? acknowledged = null)
    {
        var snapshot = !since.HasValue || since > cursor;
        var records = rows.Where(x => snapshot || x.ChangeRevision > since!.Value || acknowledged?.Contains((x.Kind, x.Id)) == true)
            .OrderBy(x => x.ChangeRevision).ThenBy(x => x.Kind).ThenBy(x => x.Id).Select(Record).ToArray();
        return new(owner, records, conflicts, cursor, snapshot);
    }

    private static SyncRecord Record(AccountSyncEntity x) => new(x.Kind, x.Id, x.Version, x.Deleted, x.Data == null ? null : JsonSerializer.Deserialize<JsonElement>(x.Data));
    private static void CheckCapacity(List<AccountSyncEntity> rows)
    {
        if (rows.Count > MaxRecords || rows.Sum(x => (long)Encoding.UTF8.GetByteCount(x.Data ?? "") + Encoding.UTF8.GetByteCount(x.Id) + 256) > MaxBytes)
            throw new SyncException("sync_capacity_exceeded", "Account synchronization capacity exceeded; no records were truncated.", 413);
    }
    private static SyncException Invalid(string message) => new("invalid_sync", message);
    private static bool Text(JsonElement e, string key, int max, bool required = true, bool allowEmpty = false)
        => !e.TryGetProperty(key, out var v) ? !required : !required && v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.String && v.GetString()!.Length <= max && (!required || allowEmpty || v.GetString()!.Length > 0);
    private static bool Date(JsonElement e, string key, bool required = true)
        => !e.TryGetProperty(key, out var v) ? !required : !required && v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out _) && (v.GetString()!.EndsWith('Z') || v.GetString()!.EndsWith("+00:00"));
    private static void NoOwnership(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object)
            foreach (var p in data.EnumerateObject())
            {
                var name = p.Name.Replace("_", "").ToLowerInvariant();
                if (name is "ownerid" or "userid" or "accountid") throw Invalid("Ownership is derived from the authenticated account.");
                NoOwnership(p.Value);
            }
        else if (data.ValueKind == JsonValueKind.Array) foreach (var item in data.EnumerateArray()) NoOwnership(item);
    }
    private static bool Card(JsonElement data, string key)
    {
        if (!data.TryGetProperty(key, out var card) || card.ValueKind != JsonValueKind.Object || !Date(card, "due") || !Date(card, "last_review", false)) return false;
        foreach (var property in new[] { "stability", "difficulty", "elapsed_days", "scheduled_days", "reps", "lapses", "state" })
            if (!card.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0) return false;
        foreach (var property in new[] { "reps", "lapses", "state", "learning_steps" })
            if (card.TryGetProperty(property, out var value) && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)) return false;
        return card.GetProperty("state").GetInt32() <= 3 && card.GetProperty("difficulty").GetDouble() <= 10;
    }
    private static void Validate(SyncChange change)
    {
        if (change == null || change.Kind is not ("deck" or "word") || string.IsNullOrWhiteSpace(change.Id) || change.Id.Length > 128 || change.Id.Any(char.IsControl) || change.BaseVersion < 0)
            throw Invalid("Invalid kind, ID or version.");
        if (change.Deleted)
        {
            if (change.Data is { ValueKind: not JsonValueKind.Null }) throw Invalid("Deleted records must have null data.");
            return;
        }
        if (change.Data is not { ValueKind: JsonValueKind.Object } data || Encoding.UTF8.GetByteCount(data.GetRawText()) > 65536) throw Invalid("Record data must be a bounded object.");
        NoOwnership(data);
        if (!Date(data, "created_at")) throw Invalid("created_at must be a UTC timestamp.");
        if (change.Kind == "deck")
        {
            if (!Text(data, "name", 512) || !Text(data, "description", 8192, false) || !Text(data, "native_language", 64) || !Text(data, "learning_language", 64)
                || !data.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("Invalid deck data.");
        }
        else
        {
            if (!Text(data, "deck_id", 128) || !Text(data, "word", 8192) || !Text(data, "translation", 8192, allowEmpty: true)
                || !Text(data, "pronunciation", 8192, false) || !Text(data, "notes", 16384, false) || !Card(data, "card") || !Card(data, "reverse")) throw Invalid("Invalid word or review card data.");
            foreach (var language in new[] { "native_language", "learning_language" })
                if (data.TryGetProperty(language, out _) && !Text(data, language, 64)) throw Invalid("Invalid word language.");
            if (data.TryGetProperty("examples", out var examples) && (examples.ValueKind != JsonValueKind.Array || examples.GetArrayLength() > 100 || examples.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Length > 8192))) throw Invalid("Invalid examples.");
        }
    }
}
