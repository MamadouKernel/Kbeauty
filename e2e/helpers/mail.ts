import { MAILPIT_URL } from './env';

interface MailpitMessage {
  readonly ID: string;
  readonly Subject: string;
}

/** Waits for a message to `to` whose subject matches, and returns its text body (Mailpit catches every e-mail in development). */
export async function waitForMail(to: string, subject: RegExp, timeoutMs = 30_000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const search = await fetch(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`);
    if (search.ok) {
      const found = ((await search.json()) as { messages?: MailpitMessage[] }).messages ?? [];
      const message = found.find((m) => subject.test(m.Subject));
      if (message) {
        const detail = await fetch(`${MAILPIT_URL}/api/v1/message/${message.ID}`);
        return ((await detail.json()) as { Text: string }).Text;
      }
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`No e-mail "${subject}" for ${to} within ${timeoutMs} ms`);
}

/** The first `token=` value of a link in the message. */
export function tokenIn(text: string): string {
  const match = /[?&]token=([^&\s"<>]+)/.exec(text);
  if (!match) {
    throw new Error('No token link in the e-mail');
  }
  return decodeURIComponent(match[1]);
}
