using RazorPages.Models;

namespace RazorPages.Services;

public class InMemoryTodoService : ITodoService
{
    private static readonly Lock _gate = new();
    private static readonly List<TodoItem> _items =
    [
        new TodoItem
        {
            Id = 1,
            Title = "Review docs",
            DueDate = DateTime.Today.AddDays(1),
            IsCompleted = false
        },
        new TodoItem
        {
            Id = 2,
            Title = "Build first Razor Page",
            DueDate = DateTime.Today.AddDays(2),
            IsCompleted = true
        }
    ];

    public IReadOnlyList<TodoItem> GetAll()
    {
        lock (_gate)
        {
            return _items
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.Title)
                .ToList();
        }
    }

    public TodoItem Add(TodoItem item)
    {
        lock (_gate)
        {
            item.Id = _items.Count == 0 ? 1 : _items.Max(i => i.Id) + 1;
            _items.Add(item);
            return item;
        }
    }

    public void ToggleComplete(int id)
    {
        lock (_gate)
        {
            var match = _items.FirstOrDefault(i => i.Id == id);
            if (match is null)
            {
                return;
            }

            match.IsCompleted = !match.IsCompleted;
        }
    }

    public void Delete(int id)
    {
        lock (_gate)
        {
            var match = _items.FirstOrDefault(i => i.Id == id);
            if (match is not null)
            {
                _items.Remove(match);
            }
        }
    }
}
