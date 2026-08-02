import { test } from 'node:test';
import * as assert from 'node:assert';
import { resolveDialect } from './dialect';

test('resolveDialect returns sqlite when only SQuiL.Sqlite is referenced', () => {
  const csproj = `<Project><ItemGroup><PackageReference Include="SQuiL.Sqlite" Version="1.0.0" /></ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlite');
});

test('resolveDialect returns sqlserver when only SQuiL.SqlServer is referenced', () => {
  const csproj = `<Project><ItemGroup><PackageReference Include="SQuiL.SqlServer" Version="1.0.0" /></ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlserver');
});

test('resolveDialect returns postgres when only SQuiL.Postgres is referenced', () => {
  const csproj = `<Project><ItemGroup><PackageReference Include="SQuiL.Postgres" Version="1.0.0" /></ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'postgres');
});

test('resolveDialect defaults to sqlserver when neither package is referenced', () => {
  const csproj = `<Project><ItemGroup><PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" /></ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlserver');
});

test('resolveDialect defaults to sqlserver when BOTH packages are referenced (no explicit marker)', () => {
  const csproj = `<Project><ItemGroup>
    <PackageReference Include="SQuiL.Sqlite" Version="1.0.0" />
    <PackageReference Include="SQuiL.SqlServer" Version="1.0.0" />
  </ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlserver');
});

test('resolveDialect defaults to sqlserver when csproj text is undefined', () => {
  assert.strictEqual(resolveDialect(undefined), 'sqlserver');
});

test('resolveDialect is case-insensitive on the PackageReference markup', () => {
  const csproj = `<Project><ItemGroup><packagereference include="SQuiL.Sqlite" version="1.0.0" /></ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlite');
});

test('resolveDialect defaults to sqlserver when Postgres AND SqlServer are both referenced (no explicit marker)', () => {
  const csproj = `<Project><ItemGroup>
    <PackageReference Include="SQuiL.Postgres" Version="1.0.0" />
    <PackageReference Include="SQuiL.SqlServer" Version="1.0.0" />
  </ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlserver');
});

test('resolveDialect defaults to sqlserver when all three providers are referenced', () => {
  const csproj = `<Project><ItemGroup>
    <PackageReference Include="SQuiL.Sqlite" Version="1.0.0" />
    <PackageReference Include="SQuiL.SqlServer" Version="1.0.0" />
    <PackageReference Include="SQuiL.Postgres" Version="1.0.0" />
  </ItemGroup></Project>`;
  assert.strictEqual(resolveDialect(csproj), 'sqlserver');
});
