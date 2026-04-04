using TodoApi.Models;
using Microsoft.EntityFrameworkCore;

namespace TodoApi.Services;

public class TodoService : ITodoService
{
    private readonly TodoContext _context;

    public TodoService(TodoContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TodoItem>> GetTodoItems()
    {
        return await _context.TodoItems.ToListAsync();
    }

    public async Task<TodoItem?> GetTodoItem(long id)
    {
        return await _context.TodoItems.FindAsync(id);
    }

    public async Task<TodoItem> PostTodoItem(CreateTodoItemDto dto)
    {
        var item = new TodoItem
        {
            Name = dto.Name,
            IsComplete = dto.IsComplete
        };

        _context.TodoItems.Add(item);
        await _context.SaveChangesAsync();

        return item;
    }

    public async Task UpdateTodoItem(long id, UpdateTodoItemDto dto)
    {
        var item = new TodoItem
        {
            Id = id,
            Name = dto.Name,
            IsComplete = dto.IsComplete
        };

        _context.Entry(item).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.TodoItems.AnyAsync(e => e.Id == id))
            {
                throw new KeyNotFoundException($"TodoItem {id} not found.");
            }

            throw;
        }
    }

    public async Task DeleteTodoItem(long id)
    {
        var todoItem = await _context.TodoItems.FindAsync(id);
        if (todoItem == null)
        {
            throw new KeyNotFoundException($"TodoItem {id} not found.");
        }

        _context.TodoItems.Remove(todoItem);
        await _context.SaveChangesAsync();
    }
}
