# SQuiL SQL Editor — VS Code Extension

Language support for [SQuiL](https://github.com/daemogar/SQuiL), the C# source generator ORM that converts annotated SQL files into strongly-typed C# code.

---

## Features

| Feature | Details |
|---|---|
| **Syntax highlighting** | SQuiL variable roles colour-coded distinctly from plain SQL |
| **IntelliSense** | Completions for `@Param_`, `@Return_` patterns, SQL keywords, types, and file scaffolding snippets |
| **Hover info** | Shows the variable's role, SQL type, generated C# type, and target record |
| **Linting** | Flags missing `USE` statements, duplicate `USE`, unknown variable names, and missing semicolons |
| **Preview Generated C#** | Opens a side-by-side view of the C# code SQuiL will generate for the current file |
| **Build Project** | Runs `dotnet build` on the nearest `.csproj`/`.sln`, triggering the source generator |

---

## File Setup

SQuiL files are standard `.sql` files in your project. The extension uses the **`squil`** language ID to activate. You have two options:

### Option A — Use the `.squil` extension (recommended)
Rename your query files to `.squil`. The extension activates automatically.

### Option B — Keep `.sql`, associate the folder
Add this to your **workspace** `.vscode/settings.json`:

```json
{
  "files.associations": {
    "**/Queries/*.sql": "squil"
  }
}
```

---

## SQuiL Variable Naming Conventions

The highlighting, hover info, and linting all understand these patterns:

| Variable pattern | Role | Generated C# |
|---|---|---|
| `@Param_Name` | Input scalar | Property on `*Request` record |
| `@Params_Name` | Input table-valued | `IEnumerable<NameItem>` on `*Request` |
| `@Param_Name TABLE(...)` | Input object | `NameItem` on `*Request` |
| `@Return_Name` | Output scalar | Property on `*Response` record |
| `@Returns_Name` | Output table | `IEnumerable<NameItem>` on `*Response` |
| `@Return_Name TABLE(...)` | Output object | `NameItem` on `*Response` |
| `@Debug` | Debug flag | Not emitted as C# |
| `@EnvironmentName` | Environment | Not emitted as C# |

---

## Example SQuiL File

```sql
--Name: GetUsersByDepartment

DECLARE @Param_DepartmentId INT;
DECLARE @Param_ActiveOnly BIT;
DECLARE @Returns_Users TABLE (
    Id INT,
    Name NVARCHAR(100),
    Email NVARCHAR(255)
);

USE [HrDatabase];

SET @Returns_Users = (
    SELECT u.Id, u.Name, u.Email
    FROM Users u
    WHERE u.DepartmentId = @Param_DepartmentId
      AND (@Param_ActiveOnly = 0 OR u.IsActive = 1)
);

SELECT * FROM @Returns_Users;
```

The **Preview Generated C#** command (`SQuiL: Preview Generated C#`) on this file produces:

```csharp
public record GetUsersByDepartmentRequest(int DepartmentId, bool ActiveOnly);
public record UsersItem(int Id, string Name, string Email);
public record GetUsersByDepartmentResponse(IEnumerable<UsersItem> Users);

[SQuiLQuery(QueryFiles.GetUsersByDepartment)]
public partial class GetUsersByDepartmentDataContext : SQuiLBaseDataContext
{
    public partial Task<GetUsersByDepartmentResponse> GetUsersByDepartmentAsync(
        GetUsersByDepartmentRequest request);
}
```

---

## Commands

| Command | Description |
|---|---|
| `SQuiL: Preview Generated C#` | Opens a side-by-side C# preview (editor title button or right-click menu) |
| `SQuiL: Build Project (Trigger Source Generator)` | Runs `dotnet build` on the nearest project file |

---

## Extension Settings

| Setting | Default | Description |
|---|---|---|
| `squil.dotnetPath` | `"dotnet"` | Path to the `dotnet` CLI, if not on PATH |

---

## Installation (Development)

```bash
cd squil-vscode
npm install
npm run compile
```

Then press **F5** in VS Code to open an Extension Development Host with the extension loaded.

To package for distribution:
```bash
npm run package   # produces squil-editor-x.x.x.vsix
```

Install the `.vsix` via **Extensions → Install from VSIX…**.
