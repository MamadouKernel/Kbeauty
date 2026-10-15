import { createHmac } from 'node:crypto';

const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
const STEP_SECONDS = 30;

function base32Decode(input: string): Buffer {
  const clean = input.replace(/[\s=]/g, '').toUpperCase();
  let bits = 0;
  let value = 0;
  const bytes: number[] = [];
  for (const char of clean) {
    value = (value << 5) | ALPHABET.indexOf(char);
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(bytes);
}

/** RFC 6238 code (SHA-1, 6 digits) for a 30-second step. */
export function codeAt(secret: string, step: number): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const hmac = createHmac('sha1', base32Decode(secret)).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  const binary =
    ((hmac[offset] & 0x7f) << 24) |
    (hmac[offset + 1] << 16) |
    (hmac[offset + 2] << 8) |
    hmac[offset + 3];
  return String(binary % 1_000_000).padStart(6, '0');
}

const lastStepBySecret = new Map<string, number>();

/**
 * A code the API will accept now. The API refuses a step that is not newer than the last one it accepted (replay protection)
 * and tolerates one step either side of the clock, so each secret can be used about three times in 90 seconds; past that this
 * waits for the next step instead of failing.
 */
export async function freshCode(secret: string): Promise<string> {
  for (;;) {
    const now = Math.floor(Date.now() / 1000 / STEP_SECONDS);
    const step = Math.max(now - 1, (lastStepBySecret.get(secret) ?? -1) + 1);
    if (step <= now + 1) {
      lastStepBySecret.set(secret, step);
      return codeAt(secret, step);
    }
    await new Promise((resolve) => setTimeout(resolve, 2_000));
  }
}
