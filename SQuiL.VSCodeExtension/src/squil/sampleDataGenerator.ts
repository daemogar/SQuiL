import { SQuiLVariable, TableColumn } from './parser';

/** Generates an INSERT INTO block with `count` sample rows for a table variable. */
export function generateSampleInsert(variable: SQuiLVariable, count: number): string {
  if (!variable.columns || variable.columns.length === 0) return '';

  const cols = variable.columns;
  const colNames = cols.map(c => c.name).join(', ');

  const rows: string[] = [];
  for (let i = 1; i <= count; i++) {
    const values = cols.map(c => sampleValue(c, i)).join(', ');
    rows.push(`    (${values})`);
  }

  return [
    `Insert Into ${variable.rawName} (${colNames})`,
    `Values`,
    rows.join(',\n') + ';',
  ].join('\n');
}

/** Matches the start of an `Insert Into @rawName` statement, case-insensitive. */
function insertIntoRegex(rawName: string): RegExp {
  // Escape the leading @ and any other regex metachars in the variable name.
  const escaped = rawName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`^\\s*insert\\s+into\\s+${escaped}\\b`, 'i');
}

/** Returns true if an `Insert Into @rawName ...` statement exists anywhere in `text`. */
export function sampleDataExists(text: string, rawName: string): boolean {
  const re = insertIntoRegex(rawName);
  return text.split('\n').some(line => re.test(line));
}

/**
 * Finds the line range (inclusive) of an existing sample data block for `rawName`.
 * The block starts at the `Insert Into @rawName ...` line and ends at the first
 * subsequent line whose trimmed content ends with `;` (the statement terminator).
 * Returns `{ startLine, endLine }` or `undefined` if not found.
 */
export function findSampleDataLines(
  lines: string[],
  rawName: string,
): { startLine: number; endLine: number } | undefined {
  const re = insertIntoRegex(rawName);

  for (let i = 0; i < lines.length; i++) {
    if (!re.test(lines[i])) continue;

    // Start line found; scan for the terminating ';'. If the start line itself
    // ends with ';', it's a single-line insert.
    for (let j = i; j < lines.length; j++) {
      if (lines[j].trimEnd().endsWith(';')) {
        return { startLine: i, endLine: j };
      }
    }
    // No terminator found — treat the start line alone as the block.
    return { startLine: i, endLine: i };
  }
  return undefined;
}

// ─── Type-aware sample value generation ──────────────────────────────────

function sampleValue(col: TableColumn, index: number): string {
  const base = col.sqlType.toLowerCase().replace(/\s*\(.*\)/, '').trim();

  switch (base) {
    case 'int':
    case 'bigint':
    case 'smallint':
    case 'tinyint':
      return String(index);

    case 'bit':
      return '1';

    case 'decimal':
    case 'numeric':
    case 'float':
    case 'real':
    case 'money':
    case 'smallmoney':
      return `${index}.00`;

    case 'varchar':
    case 'nvarchar':
    case 'char':
    case 'nchar':
    case 'text':
    case 'ntext':
      return `'${col.name} ${index}'`;

    case 'datetime':
    case 'datetime2':
    case 'smalldatetime':
      return `'2024-01-${String(index).padStart(2, '0')} 00:00:00'`;

    case 'date':
      return `'2024-01-${String(index).padStart(2, '0')}'`;

    case 'time':
      return `'${String(index).padStart(2, '0')}:00:00'`;

    case 'datetimeoffset':
      return `'2024-01-${String(index).padStart(2, '0')} 00:00:00 +00:00'`;

    case 'uniqueidentifier':
      return 'NewID()';

    case 'varbinary':
    case 'binary':
    case 'image':
      return '0x00';

    case 'xml':
      return `'<root>${index}</root>'`;

    default:
      return col.nullable ? 'Null' : `'${col.name}_${index}'`;
  }
}
