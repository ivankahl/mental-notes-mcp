using System.Collections.Generic;
using System.Globalization;
using MentalNoteMcp.Models;
using Microsoft.Data.Sqlite;

namespace MentalNoteMcp.Data;

public class MentalNoteStore
{
    private readonly string _connectionString;

    public MentalNoteStore()
    {
        var dbPath = Environment.GetEnvironmentVariable("MENTAL_NOTES_DB_PATH");

        if (string.IsNullOrWhiteSpace(dbPath))
        {
            dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mental-notes",
                "mental-notes.db");
        }

        dbPath = Path.GetFullPath(dbPath);

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        EnsureDatabase();
    }

    public MentalNote Create(string title, string? details, DateTimeOffset noteDateTime)
    {
        using var connection = OpenConnection();

        var createdAt = DateTimeOffset.Now;

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MentalNotes (Title, Details, NoteDateTime, IsDone, CompletedAt, CreatedAt)
            VALUES ($title, $details, $noteDateTime, 0, NULL, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$details", (object?)details ?? DBNull.Value);
        command.Parameters.AddWithValue("$noteDateTime", FormatDateTime(noteDateTime));
        command.Parameters.AddWithValue("$createdAt", createdAt);

        var id = (long)(command.ExecuteScalar() ?? 0L);

        return new MentalNote
        {
            Id = id,
            Title = title,
            Details = details,
            NoteDateTime = noteDateTime,
            IsDone = false,
            CompletedAt = null,
            CreatedAt = createdAt
        };
    }

    public IReadOnlyList<MentalNote> Search(DateTime? date, DateTime? startDate, DateTime? endDate, DateTimeOffset? timestamp, bool? done)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        var conditions = new List<string>();

        if (date.HasValue)
        {
            conditions.Add("NoteDateTime >= $dateStart AND NoteDateTime < $dateEndExclusive");
            command.Parameters.AddWithValue("$dateStart", date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00");
            command.Parameters.AddWithValue("$dateEndExclusive", date.Value.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00");
        }

        if (startDate.HasValue)
        {
            conditions.Add("NoteDateTime >= $rangeStart");
            command.Parameters.AddWithValue("$rangeStart", startDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00");
        }

        if (endDate.HasValue)
        {
            conditions.Add("NoteDateTime < $rangeEndExclusive");
            command.Parameters.AddWithValue("$rangeEndExclusive", endDate.Value.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00");
        }

        if (timestamp.HasValue)
        {
            conditions.Add("substr(NoteDateTime, 1, 19) = $timestamp");
            command.Parameters.AddWithValue("$timestamp", timestamp.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }

        if (done.HasValue)
        {
            conditions.Add("IsDone = $isDone");
            command.Parameters.AddWithValue("$isDone", done.Value ? 1L : 0L);
        }

        var whereClause = conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions);

        command.CommandText = $"""
            SELECT Id, Title, Details, NoteDateTime, IsDone, CompletedAt, CreatedAt
            FROM MentalNotes
            {whereClause}
            ORDER BY NoteDateTime, Id;
            """;

        return ReadNotes(command);
    }

    public bool Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MentalNotes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public MentalNote? SetDone(long id) => SetDoneState(id, isDone: true);

    public MentalNote? SetUndone(long id) => SetDoneState(id, isDone: false);

    private MentalNote? SetDoneState(long id, bool isDone)
    {
        using var connection = OpenConnection();

        using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE MentalNotes
                SET IsDone = $isDone,
                    CompletedAt = $completedAt
                WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$isDone", isDone ? 1L : 0L);
            update.Parameters.AddWithValue("$completedAt", isDone ? DateTimeOffset.Now : DBNull.Value);
            update.Parameters.AddWithValue("$id", id);

            if (update.ExecuteNonQuery() == 0)
            {
                return null;
            }
        }

        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT Id, Title, Details, NoteDateTime, IsDone, CompletedAt, CreatedAt
            FROM MentalNotes
            WHERE Id = $id;
            """;
        select.Parameters.AddWithValue("$id", id);

        using var reader = select.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private void EnsureDatabase()
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS MentalNotes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Details TEXT,
                NoteDateTime TEXT NOT NULL,
                IsDone INTEGER NOT NULL DEFAULT 0,
                CompletedAt TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_MentalNotes_NoteDateTime ON MentalNotes (NoteDateTime);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static List<MentalNote> ReadNotes(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();

        var notes = new List<MentalNote>();
        while (reader.Read())
        {
            notes.Add(Map(reader));
        }

        return notes;
    }

    private static MentalNote Map(SqliteDataReader reader)
    {
        var noteDateTimeText = reader.GetString(3);

        return new MentalNote
        {
            Id = reader.GetInt64(0),
            Title = reader.GetString(1),
            Details = reader.IsDBNull(2) ? null : reader.GetString(2),
            NoteDateTime = DateTimeOffset.Parse(noteDateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            IsDone = reader.GetInt64(4) != 0,
            CompletedAt = reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
            CreatedAt = reader.GetDateTimeOffset(6)
        };
    }

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);
}
