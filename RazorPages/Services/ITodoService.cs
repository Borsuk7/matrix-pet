using RazorPages.Models;

namespace RazorPages.Services;

public interface ITodoService
{
    IReadOnlyList<TodoItem> GetAll();
    TodoItem Add(TodoItem item);
    void ToggleComplete(int id);
    void Delete(int id);
}
