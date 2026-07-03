/**
 * Shape-key utilities for SP0030 result-shape collision detection.
 *
 * A "shape key" is a canonical string that uniquely identifies the C# record
 * type that would be generated for a given table column list.  Two variables
 * with an identical shape key map to the same generated record and therefore
 * cannot be routed apart at runtime.
 *
 * The key is length/precision-independent — `varchar(50)` and `varchar(100)`
 * both canonicalize to `string`, same as the generator's ShapeKey computation.
 *
 * Port mirrors SQuiL.SourceGenerator's ShapeKey logic.
 */

import { TableColumn } from './parser';
import { sqlToCSharp } from './previewGenerator';

/** Canonical C# type token (strip trailing '?', mirror the generator's Token.CSharpType). */
export function canonicalType(sqlType: string): string {
  const cs = sqlToCSharp(sqlType);
  return cs.endsWith('?') ? cs.slice(0, -1) : cs;
}

/** Ordered signature: columns joined by '|', each "name:canonicalType", names lower-cased. */
export function shapeKeyOf(columns: TableColumn[]): string {
  return columns.map(c => `${c.name.toLowerCase()}:${canonicalType(c.sqlType)}`).join('|');
}
