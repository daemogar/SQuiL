# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SQuiL is a C# source generator that converts SQL files into strongly-typed C# code. It processes SQL files with specific naming conventions for variables and generates:
- Data context classes for executing queries
- Request/Response models based on SQL parameters
- Dependency injection extensions
- Enum types for queries and tables

## Building and Testing

### Build Commands
```bash
# Restore dependencies
dotnet restore

# Build the source generator
dotnet build -c Release SQuiL.SourceGenerator/SQuiL.SourceGenerator.csproj

# Build the entire solution
dotnet build SQuiL.sln

# Build a specific project
dotnet build SQuiL.Tests/SQuiL.Tests.csproj
```

### Running Tests
```bash
# Run all tests
dotnet test SQuiL.Tests/SQuiL.Tests.csproj

# Run a specific test
dotnet test SQuiL.Tests/SQuiL.Tests.csproj --filter "FullyQualifiedName~BasicIODeclareTests"
```

Tests use Verify.SourceGenerators for snapshot testing. Each test generates C# code from SQL input and compares it against verified snapshots in subdirectories.

### Example Application
```bash
# Run the example application
dotnet run --project SQuiL.Simple/SQuiL.Application.csproj
```

## Architecture

### Three-Stage Pipeline

1. **Tokenization** (`SQuiL/Tokenizer/SQuiLTokenizer.cs`)
   - Converts SQL text into tokens
   - Handles SQL keywords, identifiers, literals, comments, and special constructs
   - Token types are defined in `TokenType.cs`

2. **Parsing** (`SQuiL/Parser/SQuiLParser.cs`)
   - Transforms tokens into CodeBlocks
   - Identifies variable naming patterns to determine input/output semantics
   - Recognizes DECLARE statements, USE statements, and SQL body

3. **Code Generation** (`SQuiL/Generator/SQuiLGenerator.cs`)
   - Implements `IIncrementalGenerator` for Roslyn source generation
   - Generates C# classes from CodeBlocks
   - Creates data context methods, request/response models, and DI extensions

### SQL Variable Naming Conventions

The parser recognizes specific naming patterns in SQL `DECLARE` statements:

- `@Param_<name>` → Input scalar parameter
- `@Params_<name>` → Input table-valued parameter (list)
- `@Param_<name> table(...)` → Input object parameter
- `@Return_<name>` → Output scalar variable
- `@Returns_<name>` → Output table (list)
- `@Return_<name> table(...)` → Output object
- `@Debug` / `@EnvironmentName` → Special variables (not parameters)
- `@Error` / `@Errors` → Error handling variables

These conventions determine the signature of generated C# methods.

### Generated Code Structure

For a SQL file `MyQuery.sql`, the generator creates:
- `<Namespace>.<Context>DataContext.MyQueryDataContext.g.cs` - Main method to execute query
- `<Namespace>.<Context>DataContext.MyQueryRequest.g.cs` - Request model from `@Param*` variables
- `<Namespace>.<Context>DataContext.MyQueryResponse.g.cs` - Response model from `@Return*` variables
- Partial classes for custom tables if `[SQuiLTable]` attributes are used

### Key Components

- **SQuiLDefinition** (`SQuiL/Generator/SQuiLDefinition.cs`): Represents a class with `[SQuiLQuery]` or `[SQuiLTable]` attributes
- **SQuiLDataContext** (`SQuiL/Models/SQuiLDataContext.cs`): Model representing a data context with its queries
- **SQuiLBaseDataContext**: Base class for all generated data contexts (provides connection/parameter helpers)
- **CodeBlock/CodeItem** (`SQuiL/Parser/`): Intermediate representation of parsed SQL
- **SQuiLTableMap**: Tracks custom table type mappings from `[SQuiLTable]` attributes

### Test Structure

Tests in `SQuiL.Tests/` follow a pattern:
1. Each test class (e.g., `BasicIODeclareTests.cs`) contains multiple test methods
2. Tests call `TestHelper.Verify()` with C# source and SQL query strings
3. The source generator runs on the inputs
4. Output is verified against snapshots in `<TestClass>/<TestMethod>/` subdirectories
5. Use `dotnet test` to run tests, which will fail if generated code doesn't match snapshots

To accept new snapshots after intentional changes, delete the `.verified.cs` files and re-run tests.

## SQL File Requirements

SQL files must be marked as `AdditionalFiles` in the `.csproj`:
```xml
<ItemGroup>
    <AdditionalFiles Include="**\Queries\*.sql" />
</ItemGroup>
```

SQL files should contain:
1. A `--Name: <QueryName>` comment (optional, for inline test queries)
2. `DECLARE` statements following naming conventions
3. A `USE [DatabaseName];` statement
4. SQL query body

Example:
```sql
Declare @Param_Name varchar(100);
Declare @Return_Count int;
Use MyDatabase;
Set @Return_Count = (Select Count(*) From Users Where Name = @Param_Name);
Select @Return_Count;
```

## Dependencies

Required NuGet packages:
- `Microsoft.CodeAnalysis.CSharp` (4.12.0) - Roslyn APIs for source generation
- `Microsoft.Data.SqlClient` (6.0.1) - SQL Server connectivity
- `Microsoft.Extensions.Configuration` - Configuration/connection string handling
- `Microsoft.Extensions.DependencyInjection` - DI support

Test dependencies:
- `xunit` - Test framework
- `Verify.SourceGenerators` - Snapshot testing for generated code
- `Verify.DiffPlex` - Diff visualization

## Debugging

The source generator includes debugger launch code in `SQuiLGenerator.cs`:
```csharp
#if DEBUG
    if (!System.Diagnostics.Debugger.IsAttached)
        System.Diagnostics.Debugger.Launch();
#endif
```

This is commented out by default but can be enabled for debugging generation issues.

## Connection String Configuration

Generated data contexts expect connection strings in `IConfiguration`:
```json
{
  "ConnectionStrings": {
    "SQuiLDatabase": "...",
    "CustomName": "..."
  }
}
```

The `[SQuiLQuery]` attribute accepts an optional `setting` parameter to specify which connection string to use:
```csharp
[SQuiLQuery(QueryFiles.MyQuery, setting: "CustomName")]
public partial class MyDataContext : SQuiLBaseDataContext { }
```

## Special Handling

- **Identifiers starting with SQL keywords**: The generator adds special handling for cases where an identifier starts with a keyword (see test for USE keyword)
- **DateTimeOffset → DateTime**: The project converts `datetimeoffset` SQL type to `datetime` C# type
- **Binary data**: Supports binary data input/output
- **Blank lines between data**: Adds formatting for better readability in generated code
