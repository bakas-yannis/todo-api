namespace TodoApi.Models;

public record CreateTodoItemDto(string? Name, bool IsComplete);
public record UpdateTodoItemDto(string? Name, bool IsComplete);
