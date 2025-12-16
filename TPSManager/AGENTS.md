# Repository Guidelines

- Builds and commits are performed directly by developers.
- Codex output is generated in Korean, with log files created and managed daily.
- Tasks are configured and executed using Specify integration.
- Project purpose: Develop operational tools for running a TPS game server using Blazor.

## Project Structure & Modules
- `TPSManager/`: ASP.NET Core server; entry `Program.cs`, configs in `appsettings*.json`, static assets in `wwwroot/`.
- `TPSManager.Client/`: Blazor WebAssembly client; pages under `Pages/`, shared imports `_Imports.razor`, assets in `wwwroot/`.
- `../SmugglerLib/`: Shared helpers, generated code, and ENet bindings; keep changes backward compatible for server and client consumers.
- Build outputs live in each project's `bin/` and `obj/`; leave them untracked.

## Build, Test, and Run
- Restore dependencies: `dotnet restore SmugglerServer.sln`
- Build server: `dotnet build TPSManager/TPSManager.csproj`
- Build client: `dotnet build TPSManager.Client/TPSManager.Client.csproj`
- Build shared lib: `dotnet build ../SmugglerLib/SmugglerLib.csproj`
- Run server (dev): `dotnet run --project TPSManager/TPSManager.csproj`
- Run client (wasm preview): `dotnet run --project TPSManager.Client/TPSManager.Client.csproj`

## Coding Style & Naming
- C#: 4-space indent; brace style per default C# conventions; prefer expression-bodied members for simple helpers.
- Naming: PascalCase for types/methods, camelCase for locals/parameters, `_leading` underscore for private fields when existing code does; mirror surrounding patterns.
- Organization: keep related code together (Controllers/Components/Pages); push shared logic to `SmugglerLib` to reduce duplication.
- Config: environment overrides in `appsettings.Development.json`; never hardcode secrets or endpoints.

## Testing Guidelines
- Run tests with `dotnet test`; name test projects `*.Tests` and classes `{TypeName}Tests`.
- Follow Arrange-Act-Assert; prioritize coverage for protocol/serialization changes in `SmugglerLib` and client-server boundaries.
- For UI, add component/page tests when feasible and review snapshots carefully before updating.

## Commit & PR Practices
- Commit messages: concise imperative subject (e.g., "Add TPS session validation"); include a short body for rationale or edge cases.
- PRs: state intent, key changes, and test results; link issues/work items; add screenshots for UI changes.
- Keep diffs focused; ensure build/test commands above pass before requesting review.

## Security & Configuration
- Store secrets in user secrets or environment variables; never commit keys or connection strings.
- Validate inputs across client/server boundaries; version or gate serialization changes in `SmugglerLib` to avoid breaking consumers.
