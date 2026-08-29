namespace MentalNoteMcp.Models;

public class MentalNote
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTimeOffset NoteDateTime { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
