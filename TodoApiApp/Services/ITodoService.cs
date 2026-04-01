using TodoApi.Models;

namespace TodoApi.Services;

public interface ITodoService
{
    Task<IEnumerable<TodoItem>> GetTodoItems();
    Task<TodoItem?> GetTodoItem(long id);
    Task<TodoItem> PostTodoItem(TodoItem item);
    Task UpdateTodoItem(long id, TodoItem item);
    Task DeleteTodoItem(long id);
}
