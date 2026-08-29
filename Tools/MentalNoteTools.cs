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
    [Description("Create a mental note. The note is timestamped with the current time; only pass a timestamp when the note should explicitly be logged for a different time.")]
    public string CreateMentalNote(
        [Description("Short title or summary of the mental note")] string title,
        [Description("Optional timestamp for the note, in ISO 8601 format, e.g. 2026-08-29T14:30:00 or 2026-08-29T14:30:00+02:00. Only pass this when the note should explicitly be logged for a specific time; if omitted, the current time is used. The timestamp's offset is preserved.")] string? timestamp = null,
        [Description("Optional extra details or context for the note")] string? details = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Could not create the mental note: a title is required.";
        }

        DateTimeOffset noteTime;
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            noteTime = TruncateToSeconds(DateTimeOffset.Now);
        }
        else if (!TryParseTimestamp(timestamp, out noteTime, out var timestampError))
        {
            return $"Could not create the mental note: {timestampError}";
        }

        var note = _store.Create(title.Trim(), NormalizeOptional(details), noteTime);

        return $"Mental note #{note.Id} '{note.Title}' created for {FormatDisplay(note.NoteDateTime)}.";
    }

    [McpServerTool(Name = "get_mental_notes_for_date")]
    [Description("Get all mental notes with a timestamp on a specific date (any time that day). Returns a JSON array of notes with their ids.")]
    public string GetMentalNotesForDate(
        [Description("The date to get notes for, in ISO 8601 format, e.g. 2026-08-29")] string date)
    {
        if (!TryParseDate(date, out var parsedDate, out var error))
        {
            return error;
        }

        var notes = _store.GetForDate(parsedDate);
        return FormatNotes(notes, $"No mental notes found for {parsedDate:yyyy-MM-dd}.");
    }

    [McpServerTool(Name = "get_mental_notes_for_date_range")]
    [Description("Get all mental notes with a timestamp within a range of dates, inclusive of both the start and end dates. Returns a JSON array of notes with their ids.")]
    public string GetMentalNotesForDateRange(
        [Description("Start of the date range (inclusive), in ISO 8601 format, e.g. 2026-08-29")] string startDate,
        [Description("End of the date range (inclusive), in ISO 8601 format, e.g. 2026-09-05")] string endDate)
    {
        if (!TryParseDate(startDate, out var parsedStart, out var startError))
        {
            return startError;
        }

        if (!TryParseDate(endDate, out var parsedEnd, out var endError))
        {
            return endError;
        }

        if (parsedEnd < parsedStart)
        {
            return "Could not get mental notes: the end date must not be before the start date.";
        }

        var notes = _store.GetForDateRange(parsedStart, parsedEnd);
        return FormatNotes(notes, $"No mental notes found between {parsedStart:yyyy-MM-dd} and {parsedEnd:yyyy-MM-dd}.");
    }

    [McpServerTool(Name = "get_mental_notes_for_datetime")]
    [Description("Get all mental notes at a specific timestamp (exact match on the date and time part of the timestamp). Returns a JSON array of notes with their ids.")]
    public string GetMentalNotesForDateTime(
        [Description("The exact timestamp to get notes for, in ISO 8601 format, e.g. 2026-08-29T14:30:00")] string timestamp)
    {
        if (!TryParseTimestamp(timestamp, out var parsedTimestamp, out var error))
        {
            return error;
        }

        var notes = _store.GetForDateTime(parsedTimestamp);
        return FormatNotes(notes, $"No mental notes found for {FormatDisplay(parsedTimestamp)}.");
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
