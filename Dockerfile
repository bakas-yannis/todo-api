FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy only the project files to restore
COPY ["TodoApi.sln", "./"]
COPY ["TodoApiApp/TodoApi.csproj", "TodoApiApp/"]
COPY ["TodoApi.Tests/TodoApi.Tests.csproj", "TodoApi.Tests/"]

# 2. Restore (This creates clean Linux 'obj' folders)
RUN dotnet restore "TodoApi.sln"

# 3. Copy EVERYTHING ELSE (Except what is in .dockerignore)
COPY . .

# 4. Build - This will now find the restored packages correctly
RUN dotnet build "TodoApi.sln" -c Release --no-restore

# Stage 2: Run Tests
FROM build AS test
WORKDIR "/src/TodoApi.Tests"
RUN dotnet test --no-build -c Release --verbosity normal

# Stage 3: Publish
FROM test AS publish
WORKDIR "/src/TodoApiApp"
RUN dotnet publish "TodoApi.csproj" -c Release -o /app/publish --no-restore

# Stage 4: Final Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app/publish .
# Ensure the DLL name matches your project output
ENTRYPOINT ["dotnet", "TodoApi.dll"]