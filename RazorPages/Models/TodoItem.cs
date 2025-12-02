using System.ComponentModel.DataAnnotations;

namespace RazorPages.Models;

public class TodoItem
{
    public int Id { get; set; }

    [Required]
    [StringLength(60, MinimumLength = 3)]
    public string Title { get; init; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime DueDate { get; init; }

    public bool IsCompleted { get; set; }
}
