using TodoApi.Models;

namespace TodoApi.Services;

public interface ITodoService
{
    Task<IEnumerable<TodoItem>> GetTodoItems();
    Task<TodoItem?> GetTodoItem(long id);
    Task<TodoItem> PostTodoItem(CreateTodoItemDto dto);
    Task UpdateTodoItem(long id, UpdateTodoItemDto dto);
    Task DeleteTodoItem(long id);
}
