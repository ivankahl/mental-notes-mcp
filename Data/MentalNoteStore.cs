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

    public MentalNote Create(string title, string? details, DateTime noteDateTime)
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

    public IReadOnlyList<MentalNote> GetForDate(DateTime date)
        => GetInRange(date.Date, date.Date);

    public IReadOnlyList<MentalNote> GetForDateRange(DateTime startDate, DateTime endDate)
        => GetInRange(startDate.Date, endDate.Date);

    public IReadOnlyList<MentalNote> GetForDateTime(DateTime dateTime)
    {
        using var connection = OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Details, NoteDateTime, IsDone, CompletedAt, CreatedAt
            FROM MentalNotes
            WHERE NoteDateTime = $noteDateTime
            ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$noteDateTime", FormatDateTime(dateTime));

        return ReadNotes(command);
    }

    public MentalNote? SetDone(long id) => SetDoneState(id, isDone: true);

    public MentalNote? SetUndone(long id) => SetDoneState(id, isDone: false);

    private IReadOnlyList<MentalNote> GetInRange(DateTime startDate, DateTime endDate)
    {
        using var connection = OpenConnection();

        var start = startDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00";
        var endExclusive = endDate.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00";

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Details, NoteDateTime, IsDone, CompletedAt, CreatedAt
            FROM MentalNotes
            WHERE NoteDateTime >= $start AND NoteDateTime < $endExclusive
            ORDER BY NoteDateTime, Id;
            """;
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$endExclusive", endExclusive);

        return ReadNotes(command);
    }

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
            NoteDateTime = DateTime.Parse(noteDateTimeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            IsDone = reader.GetInt64(4) != 0,
            CompletedAt = reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
            CreatedAt = reader.GetDateTimeOffset(6)
        };
    }

    private static string FormatDateTime(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
