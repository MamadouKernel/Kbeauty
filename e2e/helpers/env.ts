import { execFileSync } from 'node:child_process';
import path from 'node:path';

export const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:4200';
export const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';

/** Meets the password rules (10+ characters, a letter and a digit); shared by every throw-away account. */
export const PASSWORD = 'Correct-Horse-42';

const REPOSITORY_ROOT = path.resolve(__dirname, '..', '..');

/**
 * Runs one SQL statement in the stack's PostgreSQL container. Used only to do what no API allows on purpose: promote a
 * throw-away account to Admin or SuperAdmin (the integration tests do the same straight in the database).
 */
export function sql(statement: string): string {
  return execFileSync(
    'docker',
    ['compose', 'exec', '-T', 'postgres', 'psql', '-U', 'keke', '-d', 'kekebeauty', '-tAc', statement],
    { cwd: REPOSITORY_ROOT, encoding: 'utf8', env: { ...process.env, MSYS_NO_PATHCONV: '1' } },
  ).trim();
}

export function uniqueEmail(prefix: string): string {
  return `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}@example.com`;
}
