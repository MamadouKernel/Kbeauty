/**
 * Regenerates src/app/core/api/api.generated.ts from the running API's OpenAPI document.
 *
 *   npm run api:types                       (API on http://localhost:8080)
 *   API_URL=http://localhost:5074 npm run api:types
 *
 * openapi-typescript is executed through npx at a pinned version because it still declares a
 * TypeScript 5 peer dependency while the workspace uses TypeScript 6.
 */
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const apiUrl = process.env.API_URL ?? 'http://localhost:8080';
const output = path.join('src', 'app', 'core', 'api', 'api.generated.ts');
const shell = process.platform === 'win32';

execFileSync('npx', ['--yes', 'openapi-typescript@7.9.1', `${apiUrl}/openapi/v1.json`, '-o', output], {
  stdio: 'inherit',
  shell,
});

// Generated code is not subject to the project's lint rules.
fs.writeFileSync(output, `/* eslint-disable */\n${fs.readFileSync(output, 'utf8')}`);
execFileSync('npx', ['prettier', '--write', output], { stdio: 'inherit', shell });
