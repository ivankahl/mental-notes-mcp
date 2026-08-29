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

    private const string DateTimeFormatHint = "ISO 8601 format, e.g. 2026-08-29T14:30:00 (times are treated as local time)";
    private const string DateFormatHint = "ISO 8601 format, e.g. 2026-08-29";

    private readonly MentalNoteStore _store;

    public MentalNoteTools(MentalNoteStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "create_mental_note")]
    [Description("Create a mental note scheduled for a specific date and time.")]
    public string CreateMentalNote(
        [Description("Short title or summary of the mental note")] string title,
        [Description("Date and time the note is for, in ISO 8601 format, e.g. 2026-08-29T14:30:00. Times are treated as local time.")] string noteDateTime,
        [Description("Optional extra details or context for the note")] string? details = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Could not create the mental note: a title is required.";
        }

        if (!TryParseDateTime(noteDateTime, out var parsedDateTime, out var dateTimeError))
        {
            return $"Could not create the mental note: {dateTimeError}";
        }

        var note = _store.Create(title.Trim(), NormalizeOptional(details), parsedDateTime);

        return $"Mental note #{note.Id} '{note.Title}' created for {FormatDisplay(note.NoteDateTime)}.";
    }

    [McpServerTool(Name = "get_mental_notes_for_date")]
    [Description("Get all mental notes scheduled on a specific date (any time that day). Returns a JSON array of notes with their ids.")]
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
    [Description("Get all mental notes scheduled within a range of dates, inclusive of both the start and end dates. Returns a JSON array of notes with their ids.")]
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
    [Description("Get all mental notes scheduled at a specific date and time (exact match to the minute-level scheduled time). Returns a JSON array of notes with their ids.")]
    public string GetMentalNotesForDateTime(
        [Description("The exact date and time to get notes for, in ISO 8601 format, e.g. 2026-08-29T14:30:00. Times are treated as local time.")] string dateTime)
    {
        if (!TryParseDateTime(dateTime, out var parsedDateTime, out var error))
        {
            return error;
        }

        var notes = _store.GetForDateTime(parsedDateTime);
        return FormatNotes(notes, $"No mental notes found for {FormatDisplay(parsedDateTime)}.");
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

    private static string FormatDisplay(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseDate(string? value, out DateTime date, out string error)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            date = parsed.LocalDateTime.Date;
            error = string.Empty;
            return true;
        }

        date = default;
        error = $"Invalid date '{value}'. Use {DateFormatHint}.";
        return false;
    }

    private static bool TryParseDateTime(string? value, out DateTime dateTime, out string error)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            var local = parsed.LocalDateTime;
            dateTime = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerSecond));
            error = string.Empty;
            return true;
        }

        dateTime = default;
        error = $"Invalid date/time '{value}'. Use {DateTimeFormatHint}.";
        return false;
    }
}
