import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  input,
  output,
  viewChild,
} from '@angular/core';

interface GoogleCredentialResponse {
  readonly credential: string;
}

interface GoogleAccountsId {
  initialize(config: {
    client_id: string;
    callback: (response: GoogleCredentialResponse) => void;
  }): void;
  renderButton(host: HTMLElement, options: Record<string, string | number>): void;
}

interface GoogleGlobal {
  readonly accounts: { readonly id: GoogleAccountsId };
}

const GIS_SCRIPT = 'https://accounts.google.com/gsi/client';

/**
 * "Continue with Google" button (Google Identity Services). Only rendered when the API reports Google
 * sign-in as enabled; the ID token it yields is verified server-side before anything is trusted.
 */
@Component({
  selector: 'app-google-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="google-host" aria-label="Continuer avec Google"></div>`,
  styles: `
    .google-host {
      display: flex;
      justify-content: center;
      min-height: 2.75rem;
    }
  `,
})
export class GoogleSignIn {
  readonly clientId = input.required<string>();
  readonly credential = output<string>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');

  constructor() {
    afterNextRender(() => void this.render());
  }

  private async render(): Promise<void> {
    try {
      await loadGoogleScript();
    } catch {
      return; // Offline or blocked: the password form remains available.
    }

    const google = (window as unknown as { google?: GoogleGlobal }).google;
    google?.accounts.id.initialize({
      client_id: this.clientId(),
      callback: (response) => this.credential.emit(response.credential),
    });
    google?.accounts.id.renderButton(this.host().nativeElement, {
      theme: 'outline',
      size: 'large',
      text: 'continue_with',
      shape: 'pill',
      locale: 'fr',
      width: 320,
    });
  }
}

function loadGoogleScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (document.querySelector(`script[src="${GIS_SCRIPT}"]`)) {
      resolve();
      return;
    }

    const script = document.createElement('script');
    script.src = GIS_SCRIPT;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error('Google Identity Services failed to load.'));
    document.head.appendChild(script);
  });
}
