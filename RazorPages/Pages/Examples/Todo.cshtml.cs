using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RazorPages.Models;
using RazorPages.Services;

namespace RazorPages.Pages.Examples;

public class TodoModel(ITodoService todoService) : PageModel
{
    public IReadOnlyList<TodoItem> Items { get; private set; } = [];

    [BindProperty]
    public TodoInput Input { get; set; } = new();

    public void OnGet()
    {
        LoadItems();
    }

    public IActionResult OnPostAdd()
    {
        if (!ModelState.IsValid)
        {
            LoadItems();
            return Page();
        }

        todoService.Add(new TodoItem
        {
            Title = Input.Title.Trim(),
            DueDate = Input.DueDate!.Value
        });

        return RedirectToPage();
    }

    public IActionResult OnPostToggle(int id)
    {
        todoService.ToggleComplete(id);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int id)
    {
        todoService.Delete(id);
        return RedirectToPage();
    }

    private void LoadItems()
    {
        Items = todoService.GetAll();
    }
}

public class TodoInput
{
    [Required]
    [StringLength(60, MinimumLength = 3)]
    [Display(Name = "Task title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateTime? DueDate { get; set; }
}
