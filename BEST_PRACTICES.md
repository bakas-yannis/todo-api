# ASP.NET Core Best Practices

---

## Dependency Injection

**Concept:** DI is a design pattern where objects receive their dependencies from an external source rather than creating them themselves. ASP.NET Core has a built-in DI container that manages object lifetimes and injects them into constructors automatically.

### Service Lifetimes

| Lifetime | Registered with | Created | Disposed | Use for |
|---|---|---|---|---|
| **Transient** | `AddTransient` | Every injection | After each use | Stateless, lightweight services |
| **Scoped** | `AddScoped` | Once per HTTP request | End of request | `DbContext`, services that use it |
| **Singleton** | `AddSingleton` | First request | App shutdown | Caches, configuration, stateless global services |

### Rules
- Register `DbContext` as **scoped** (`AddDbContext`) — one instance per HTTP request, disposed at the end. Never singleton.
- Register services as **scoped** (`AddScoped<IService, Service>()`) to match `DbContext` lifetime.
- Depend on **interfaces** (`ITodoService`), not concrete classes — enables testing and swapping implementations.
- Never inject a scoped service into a singleton — ASP.NET Core throws `InvalidOperationException` at startup to catch this.

---

## Service Layer

**Concept:** The service layer sits between the controller and the database. It encapsulates business logic, keeps controllers thin, and makes logic reusable and testable independently of HTTP.

### Responsibilities

| Layer | Responsible for | Not responsible for |
|---|---|---|
| **Controller** | HTTP routing, request/response mapping, status codes | Business logic, database access |
| **Service** | Business logic, database operations, exception translation | HTTP concepts (`ActionResult`, status codes) |
| **Repository** *(optional extra layer)* | Raw data access only | Business rules |

### Rules
- Keep **database logic in the service**, not the controller.
- Catch **EF Core-specific exceptions** (`DbUpdateConcurrencyException`) inside the service and translate them to domain exceptions (`KeyNotFoundException`) so the controller never depends on EF Core types.
- Use `AnyAsync()` instead of `FindAsync()` when checking existence after a concurrency exception — `FindAsync` checks the change tracker first and may return a stale result.

---

## Controllers

**Concept:** Controllers handle HTTP concerns only — they receive a request, call the service, and return an appropriate HTTP response. They should contain minimal logic.

### Return Types

| Return type | Use when | Example |
|---|---|---|
| `ActionResult<T>` | Action returns data | `GET` returning an entity |
| `IActionResult` | Action returns status code only | `PUT`, `DELETE` |
| `Ok(value)` | Explicit `200` with body | Required when `T` is generic (e.g. `IEnumerable<T>`) |
| `CreatedAtAction(...)` | `201` after successful `POST` | Returns location header |
| `NotFound()` | `404` — resource not found | Item doesn't exist |
| `BadRequest()` | `400` — invalid client input | ID mismatch, validation failure |
| `NoContent()` | `204` — success, no body | After `PUT`, `DELETE` |

### Rules
- `IEnumerable<T>` does not implicitly convert to `ActionResult<IEnumerable<T>>` — wrap it in `Ok()` explicitly.
- Annotate route parameters with `[FromRoute]` to ensure Swagger generates the correct OpenAPI spec.
- Catch domain exceptions (`KeyNotFoundException`) in the controller and map them to HTTP responses.

---

## DTOs (Data Transfer Objects)

**Concept:** DTOs are simple objects that define the shape of data entering or leaving the API. They decouple the API contract from the internal entity model, preventing overposting attacks and giving you control over what clients can read or write.

### Request vs Response DTOs

| Type | Direction | Purpose | Example |
|---|---|---|---|
| **Create DTO** | Client → API | Fields allowed on creation | `CreateTodoItemDto` (no `Id`) |
| **Update DTO** | Client → API | Fields allowed on update | `UpdateTodoItemDto` |
| **Response DTO** | API → Client | Fields exposed to the client | `TodoItemResponseDto` (hides audit fields) |

### Rules
- Never expose entity models directly to the API.
- Separate `CreateDto` and `UpdateDto` even if identical today — they almost always diverge.
- Use **positional records** for DTOs — immutable by default, concise, value equality and `ToString()` for free.
- Reset `Id = 0` on insert (or use a DTO without `Id`) to prevent clients controlling database-generated keys.

### DTO Patterns Used in the Market

#### 1. Manual Mapping (this project)
Map properties by hand in the service. Simple, zero dependencies, full control.
```csharp
var item = new TodoItem { Name = dto.Name, IsComplete = dto.IsComplete };
```
**Use when:** small projects, few properties, mapping logic is trivial.

#### 2. Extension / Static Factory Methods
Encapsulate mapping in extension methods or static factory methods on the DTO/entity.
```csharp
public static class CreateTodoItemDtoExtensions
{
    public static TodoItem ToEntity(this CreateTodoItemDto dto) =>
        new() { Name = dto.Name, IsComplete = dto.IsComplete };
}
```
**Use when:** mapping is reused in multiple places and you want to keep service methods clean.

#### 3. AutoMapper
Industry-standard mapping library. Convention-based — maps properties with matching names automatically.
```csharp
builder.Services.AddAutoMapper(typeof(Program));

public class TodoProfile : Profile
{
    public TodoProfile()
    {
        CreateMap<CreateTodoItemDto, TodoItem>();
        CreateMap<TodoItem, TodoItemResponseDto>();
    }
}

var item = _mapper.Map<TodoItem>(dto);
```
**Use when:** large projects with many entities/DTOs, response DTOs that reshape data, complex object graphs.
**Gotcha:** magic mapping silently skips properties when names don't match — always test mappings.

#### 4. Mapster
Faster alternative to AutoMapper with better support for records and immutable types.
```csharp
var item = dto.Adapt<TodoItem>();
```
**Use when:** performance-sensitive scenarios or when working heavily with records.

#### 5. Response DTOs
Separate output DTOs control what is returned to the client — hiding internal fields, computed properties, or related data.
```csharp
public record TodoItemResponseDto(long Id, string? Name, bool IsComplete);
```
**Use when:** entities have fields that should never be exposed (internal flags, foreign keys, audit fields).

#### 6. Validation with Data Annotations
DTOs are the right place for input validation — not entities. `[ApiController]` automatically returns `400 Bad Request` when annotations fail.
```csharp
public record CreateTodoItemDto(
    [Required][MaxLength(100)] string Name,
    bool IsComplete
);
```

#### Comparison

| Approach | Complexity | Dependencies | Best for |
|---|---|---|---|
| Manual mapping | Low | None | Small projects |
| Extension methods | Low | None | Reusable mapping, medium projects |
| AutoMapper | Medium | `AutoMapper` NuGet | Large projects, many entities |
| Mapster | Medium | `Mapster` NuGet | Performance-sensitive or record-heavy |
| Response DTOs | Low | None | Hiding internal entity fields |
| Data annotations | Low | None (built-in) | Input validation |

---

## Entity Framework Core

**Concept:** EF Core is an ORM (Object-Relational Mapper) — it maps C# classes to database tables and translates LINQ queries into SQL, so you work with objects rather than raw SQL.

### Change Tracker

The change tracker is an in-memory cache inside `DbContext` that records every entity loaded during the request. EF Core uses it to detect what changed and generate the minimum SQL needed on `SaveChangesAsync()`.

| State | Meaning | SQL on SaveChanges |
|---|---|---|
| `Unchanged` | Loaded, not modified | None |
| `Modified` | Properties changed | `UPDATE` |
| `Added` | New entity | `INSERT` |
| `Deleted` | Marked for removal | `DELETE` |
| `Detached` | Not tracked | None |

### Rules
- `DbSet<T>` properties use `= null!` to satisfy the nullable compiler — EF Core populates them at runtime.
- `EntityState.Modified` marks **all** columns dirty — generates a full `UPDATE`. Use `PATCH` with `JsonPatchDocument<T>` for partial updates.
- `FindAsync` checks the change tracker before hitting the database — unreliable after a concurrency exception.
- The change tracker is **not cleared** after a `DbUpdateConcurrencyException` — use `AnyAsync` instead.
- Since `DbContext` is scoped, the change tracker is fresh on every HTTP request — no cross-request contamination.

### EF Core Methods Cheatsheet

| Method | Hits DB? | Use for |
|---|---|---|
| `FindAsync(id)` | Only if not tracked | PK lookup, benefits from cache |
| `FirstOrDefaultAsync(e => ...)` | Always | Filtered lookup, always fresh |
| `ToListAsync()` | Always | Fetch all / filtered list |
| `AnyAsync(e => ...)` | Always | Existence check, no data fetch |
| `SaveChangesAsync()` | Always | Persist tracked changes |

---

## Nullable Reference Types

**Concept:** Nullable reference types (NRT), introduced in C# 8, make `null` explicit in the type system. The compiler tracks nullability and warns when you might dereference a null value — catching `NullReferenceException` bugs at compile time instead of runtime.

### Null Handling Patterns

| Pattern | Syntax | Use when |
|---|---|---|
| Null check | `if (item is null)` | Guard before using a nullable value |
| Null-conditional | `item?.Name` | Access member, returns null if item is null |
| Null-coalescing | `item ?? defaultValue` | Provide a fallback value |
| Null-forgiving | `item!.Name` | You are certain it's not null (use sparingly) |
| Nullable return | `TodoItem?` | Method may legitimately return null |

### Rules
- Enable `<Nullable>enable</Nullable>` in `.csproj`.
- Use `T?` when null is a valid result (e.g. `GetTodoItem` when item not found).
- Use `null!` only where runtime guarantees non-null (e.g. EF Core `DbSet` properties).
- Prefer explicit null checks over `!` — the forgiving operator suppresses warnings but adds no runtime safety.

---

## Async / Await

**Concept:** Async/await enables non-blocking I/O. When a method awaits a database call or HTTP request, the thread is released back to the pool to handle other requests rather than sitting idle. This is critical for scalable web APIs.

### Async Rules

| Rule | Why |
|---|---|
| Mark method `async Task<T>` to use `await` | Required by the compiler |
| Use `async Task` (not `async void`) for void returns | `async void` swallows exceptions |
| Prefer `Async` EF Core methods | Avoids thread blocking during DB calls |
| Don't use `.Result` or `.Wait()` | Can cause deadlocks in ASP.NET Core |
| Suffix async methods with `Async` | Convention — signals the caller to await |

---

## Middleware Order

**Concept:** The ASP.NET Core request pipeline is a chain of middleware components. Each one processes a request and either passes it to the next or short-circuits with a response. **Order matters** — middleware only has access to information set by middleware that ran before it.

### Correct Order

```
Request
  │
  ▼
UseHttpsRedirection()   — redirect HTTP → HTTPS
  │
  ▼
UseRouting()            — match URL to an endpoint (implicit in .NET 6+)
  │
  ▼
UseAuthentication()     — who are you? (if used)
  │
  ▼
UseAuthorization()      — are you allowed? (needs routing result)
  │
  ▼
MapControllers()        — execute the matched controller action
```

### Why Order Matters

| Wrong order | Consequence |
|---|---|
| `UseAuthorization` before `UseRouting` | Can't evaluate endpoint-specific `[Authorize]` policies |
| `UseAuthorization` after `MapControllers` | Request reaches controller before auth check runs |
| `UseAuthentication` after `UseAuthorization` | User identity not established before authorization check |

---

## Configuration

**Concept:** ASP.NET Core uses a layered configuration system. Multiple sources are loaded in order, and later sources override earlier ones. Environment-specific files let you change behaviour without modifying code.

### Configuration Files

| File | Read by | Deployed | Purpose |
|---|---|---|---|
| `launchSettings.json` | Dev tooling only | No | Launch profiles, sets `ASPNETCORE_ENVIRONMENT` |
| `appsettings.json` | ASP.NET Core runtime | Yes | Baseline app config, always loaded |
| `appsettings.{Env}.json` | ASP.NET Core runtime | Yes | Overrides for specific environment |
| Environment variables | ASP.NET Core runtime | Via platform | Override any setting in production |
| User secrets | Dev tooling only | No | Local secrets, never committed to source control |

### Load Order (later overrides earlier)
```
appsettings.json
  → appsettings.{Environment}.json
    → Environment variables
      → Command-line arguments
```

---

## Implicit Usings

**Concept:** `<ImplicitUsings>enable</ImplicitUsings>` automatically injects a set of common `using` directives into every file in the project — similar to a precompiled header (`pch.h`) in C++.

### Implicit vs Explicit

| | Implicit usings | Explicit usings |
|---|---|---|
| Boilerplate | Reduced | More verbose |
| Visibility of dependencies | Hidden | Immediately visible |
| Ambiguity risk | Possible | None |
| Portability of files | Fragile | Self-contained |

### Rules
- Always add explicit `using` for your **own namespaces** (e.g. `using TodoApi.Models`) — never implicitly included.
- `using` directives are **file-scoped** — they do not propagate to other files, unlike C/C++ `#include`.
- Adding `using` inside a `namespace` block scopes it to that namespace only — non-idiomatic, avoid.

---

## Project Structure

**Concept:** A well-structured project separates concerns into distinct folders, making it easy to navigate and understand what each file is responsible for.

### Typical ASP.NET Core API Layout

```
TodoApiApp/
  Controllers/        — HTTP layer, one controller per resource
  Models/             — Entity classes and DTOs
  Services/           — Business logic interfaces and implementations
  Properties/         — launchSettings.json
  Program.cs          — App bootstrap, DI registration, middleware pipeline
  appsettings.json
```

### Rules
- One interface per service (`ITodoService` / `TodoService`) — keeps the contract separate from the implementation.
- DTOs in a dedicated file (`TodoItemDtos.cs`) when there are multiple related ones.
- Controllers reference only service interfaces and model types — no direct `DbContext` usage in controllers.
- As the project grows, consider adding `Repositories/` for raw data access and `Exceptions/` for custom exception types.

## Service Layer

- Keep **database logic in the service**, not the controller. The controller's job is HTTP — mapping requests to responses.
- Catch **EF Core-specific exceptions** (`DbUpdateConcurrencyException`) inside the service and translate them to domain exceptions (`KeyNotFoundException`) so the controller never depends on EF Core types.
- Use `AnyAsync()` instead of `FindAsync()` when checking existence after a concurrency exception — `FindAsync` checks the change tracker first and may return a stale result.

## Controllers

- Use `ActionResult<T>` as the return type when the action returns data — enables implicit conversion and correct OpenAPI schema generation.
- Use `IActionResult` when the action returns only status codes (no body), e.g. PUT, DELETE.
- `IEnumerable<T>` does not implicitly convert to `ActionResult<IEnumerable<T>>` — wrap it in `Ok()` explicitly.
- Annotate route parameters with `[FromRoute]` to ensure Swagger generates the correct OpenAPI spec.
- Catch domain exceptions (`KeyNotFoundException`) in the controller and map them to HTTP responses (`NotFound()`).

## DTOs (Data Transfer Objects)

- Never expose entity models directly to the API — use DTOs to control what clients can send.
- Separate `CreateDto` and `UpdateDto` even if identical today — they almost always diverge (e.g. `[Required]` on create but optional on update).
- Use **positional records** for DTOs — immutable by default, concise, with value equality and `ToString()` for free.
- Reset `Id = 0` on insert (or use a DTO without `Id`) to prevent clients from controlling database-generated keys.

### DTO Patterns Used in the Market

#### 1. Manual Mapping (this project)
Map properties by hand in the service. Simple, zero dependencies, full control.
```csharp
var item = new TodoItem { Name = dto.Name, IsComplete = dto.IsComplete };
```
**Use when:** small projects, few properties, mapping logic is trivial.

#### 2. Extension / Static Factory Methods
Encapsulate mapping in extension methods or static factory methods on the DTO/entity.
```csharp
// Extension method on DTO
public static class CreateTodoItemDtoExtensions
{
    public static TodoItem ToEntity(this CreateTodoItemDto dto) =>
        new() { Name = dto.Name, IsComplete = dto.IsComplete };
}

// Or static factory on the entity
public static TodoItem FromDto(CreateTodoItemDto dto) =>
    new() { Name = dto.Name, IsComplete = dto.IsComplete };
```
**Use when:** mapping is reused in multiple places and you want to keep service methods clean.

#### 3. AutoMapper
Industry-standard mapping library. Convention-based — maps properties with matching names automatically.
```csharp
// Registration
builder.Services.AddAutoMapper(typeof(Program));

// Profile
public class TodoProfile : Profile
{
    public TodoProfile()
    {
        CreateMap<CreateTodoItemDto, TodoItem>();
        CreateMap<TodoItem, TodoItemResponseDto>();
    }
}

// Usage
var item = _mapper.Map<TodoItem>(dto);
```
**Use when:** large projects with many entities/DTOs, response DTOs that reshape data, complex object graphs.
**Gotcha:** magic mapping can silently skip properties when names don't match — always test mappings.

#### 4. Mapster
Faster alternative to AutoMapper with a similar API, but with better support for records and immutable types.
```csharp
var item = dto.Adapt<TodoItem>();
```
**Use when:** performance-sensitive scenarios or when working heavily with records.

#### 5. Response DTOs (separate from request DTOs)
Many APIs also use dedicated response (output) DTOs to control what is returned to the client — hiding internal fields, computed properties, or related data.
```csharp
public record TodoItemResponseDto(long Id, string? Name, bool IsComplete);

// In service
return new TodoItemResponseDto(item.Id, item.Name, item.IsComplete);
```
**Use when:** the entity has fields that should never be exposed (e.g. internal flags, foreign keys, audit fields).

#### 6. Validation with Data Annotations
DTOs are the right place to put input validation — not entities.
```csharp
public record CreateTodoItemDto(
    [Required][MaxLength(100)] string Name,
    bool IsComplete
);
```
ASP.NET Core's `[ApiController]` attribute automatically returns `400 Bad Request` with validation errors when annotations fail — no manual `ModelState.IsValid` check needed.

#### Comparison

| Approach | Complexity | Dependencies | Best for |
|---|---|---|---|
| Manual mapping | Low | None | Small projects |
| Extension methods | Low | None | Reusable mapping, medium projects |
| AutoMapper | Medium | `AutoMapper` NuGet | Large projects, many entities |
| Mapster | Medium | `Mapster` NuGet | Performance-sensitive or record-heavy |
| Response DTOs | Low | None | Hiding internal entity fields |
| Data annotations | Low | None (built-in) | Input validation |

## Entity Framework Core

- `DbSet<T>` properties on `DbContext` use `= null!` to satisfy the nullable compiler — EF Core guarantees they are populated at runtime.
- `EntityState.Modified` marks all columns as dirty and generates a full `UPDATE`. Use `PATCH` with `JsonPatchDocument<T>` if partial updates matter.
- `FindAsync` checks the change tracker before hitting the database — useful for repeated lookups within the same request, but unreliable after a concurrency exception.
- The change tracker is **not cleared** after a `DbUpdateConcurrencyException` — the failed entity remains tracked with its previous state.
- Since `DbContext` is scoped, the change tracker is fresh on every new HTTP request — no cross-request contamination.

## Nullable Reference Types

- Enable `<Nullable>enable</Nullable>` in `.csproj` to catch null dereferences at compile time.
- Use `T?` return types when a method may legitimately return null (e.g. `GetTodoItem` when not found).
- Use `null!` only where you can guarantee the value will be non-null at runtime (e.g. EF Core `DbSet` properties).
- Prefer null checks (`if (item is null)`) or null-conditional (`item?.Name`) over the null-forgiving operator (`item!.Name`).

## Async / Await

- Mark methods `async Task<T>` whenever using `await` inside them.
- Use `async Task` (not `async void`) for methods that return nothing — `async void` swallows exceptions.
- Prefer `Async` variants of EF Core methods (`ToListAsync`, `FindAsync`, `SaveChangesAsync`) to avoid blocking threads.

## Middleware Order

Correct order in `Program.cs`:

```
UseHttpsRedirection()
UseRouting()         ← implicit in .NET 6+ but must precede authorization
UseAuthentication()  ← if used
UseAuthorization()   ← must come after routing, before endpoints
MapControllers()
```

## Configuration

| File | Read by | Deployed | Purpose |
|---|---|---|---|
| `launchSettings.json` | Dev tooling only | No | Launch profiles, sets `ASPNETCORE_ENVIRONMENT` |
| `appsettings.json` | ASP.NET Core runtime | Yes | Baseline app config |
| `appsettings.{Env}.json` | ASP.NET Core runtime | Yes | Environment-specific overrides |

## Implicit Usings

- `<ImplicitUsings>enable</ImplicitUsings>` injects common namespaces into every file — fine for small projects.
- Always add explicit `using` for your own namespaces (e.g. `using TodoApi.Models`) — they are never implicitly included.
- `using` directives are **file-scoped** — they do not propagate to other files.

## Project Structure

- One interface per service (`ITodoService` / `TodoService`).
- DTOs in a dedicated file (`TodoItemDtos.cs`) when there are multiple related ones.
- Controllers reference only service interfaces and model types — no direct `DbContext` usage.
