using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MentalNoteMcp.Data;
using ModelContextProtocol.Server;

namespace MentalNoteMcp.Tools;

[McpServerToolType]
public class MentalNoteTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string TimestampFormatHint = "ISO 8601 timestamp, e.g. 2026-08-29T14:30:00 or 2026-08-29T14:30:00+02:00";
    private const string DateFormatHint = "ISO 8601 format, e.g. 2026-08-29";

    private readonly MentalNoteStore _store;

    public MentalNoteTools(MentalNoteStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "create_mental_note")]
    [Description("Create a mental note. The note is always timestamped with the current time; there is no option to pass or override the timestamp.")]
    public string CreateMentalNote(
        [Description("Short title or summary of the mental note")] string title,
        [Description("Optional extra details or context for the note")] string? details = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Could not create the mental note: a title is required.";
        }

        var noteTime = TruncateToSeconds(DateTimeOffset.Now);

        var note = _store.Create(title.Trim(), NormalizeOptional(details), noteTime);

        return $"Mental note #{note.Id} '{note.Title}' created for {FormatDisplay(note.NoteDateTime)}.";
    }

    [McpServerTool(Name = "search_mental_notes")]
    [Description("Search mental notes by any combination of filters: a specific date, an inclusive date range, an exact timestamp, and/or completion status. Filters you do not provide are ignored. Returns a JSON array of notes with their ids.")]
    public string SearchMentalNotes(
        [Description("Optional. Match notes on a specific date, ISO 8601, e.g. 2026-08-29.")] string? date = null,
        [Description("Optional. Start of an inclusive date range, ISO 8601, e.g. 2026-08-29.")] string? startDate = null,
        [Description("Optional. End of an inclusive date range, ISO 8601, e.g. 2026-09-05.")] string? endDate = null,
        [Description("Optional. Exact timestamp to match, ISO 8601, e.g. 2026-08-29T14:30:00.")] string? timestamp = null,
        [Description("Optional. true for notes marked done, false for notes not yet done; omit to include both.")] bool? done = null)
    {
        DateTime? parsedDate = null;
        DateTime? parsedStart = null;
        DateTime? parsedEnd = null;
        DateTimeOffset? parsedTimestamp = null;

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!TryParseDate(date, out var d, out var dateError)) return dateError;
            parsedDate = d;
        }

        if (!string.IsNullOrWhiteSpace(startDate))
        {
            if (!TryParseDate(startDate, out var d, out var startError)) return startError;
            parsedStart = d;
        }

        if (!string.IsNullOrWhiteSpace(endDate))
        {
            if (!TryParseDate(endDate, out var d, out var endError)) return endError;
            parsedEnd = d;
        }

        if (parsedStart.HasValue && parsedEnd.HasValue && parsedEnd.Value < parsedStart.Value)
        {
            return "Could not search mental notes: the end date must not be before the start date.";
        }

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            if (!TryParseTimestamp(timestamp, out var t, out var timestampError)) return timestampError;
            parsedTimestamp = t;
        }

        var notes = _store.Search(parsedDate, parsedStart, parsedEnd, parsedTimestamp, done);
        return FormatNotes(notes, "No mental notes found matching the given filters.");
    }

    [McpServerTool(Name = "delete_mental_note")]
    [Description("Permanently delete a mental note by its id. This hard-deletes the note and cannot be undone.")]
    public string DeleteMentalNote(
        [Description("The id of the mental note to delete")] long id)
    {
        var deleted = _store.Delete(id);
        return deleted
            ? $"Mental note #{id} deleted."
            : $"No mental note found with id {id}.";
    }

    [McpServerTool(Name = "mark_mental_note_done")]
    [Description("Mark a mental note as done (cross it off). Use the note's id.")]
    public string MarkMentalNoteDone(
        [Description("The id of the mental note to mark as done")] long id)
    {
        var note = _store.SetDone(id);
        return note is null
            ? $"No mental note found with id {id}."
            : $"Mental note #{note.Id} '{note.Title}' marked as done.";
    }

    [McpServerTool(Name = "mark_mental_note_undone")]
    [Description("Revert a mental note that was marked as done back to not done. Use the note's id.")]
    public string MarkMentalNoteUndone(
        [Description("The id of the mental note to mark as not done")] long id)
    {
        var note = _store.SetUndone(id);
        return note is null
            ? $"No mental note found with id {id}."
            : $"Mental note #{note.Id} '{note.Title}' marked as not done.";
    }

    private static string FormatNotes(IReadOnlyList<Models.MentalNote> notes, string emptyMessage)
    {
        if (notes.Count == 0)
        {
            return emptyMessage;
        }

        return JsonSerializer.Serialize(notes, JsonOptions);
    }

    private static string FormatDisplay(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value)
        => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private static bool TryParseDate(string? value, out DateTime date, out string error)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed.Date;
            error = string.Empty;
            return true;
        }

        date = default;
        error = $"Invalid date '{value}'. Use {DateFormatHint}.";
        return false;
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp, out string error)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            timestamp = TruncateToSeconds(parsed);
            error = string.Empty;
            return true;
        }

        timestamp = default;
        error = $"Invalid timestamp '{value}'. Use {TimestampFormatHint}.";
        return false;
    }
}
