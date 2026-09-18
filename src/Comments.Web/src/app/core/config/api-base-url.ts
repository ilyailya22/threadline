import { InjectionToken } from '@angular/core';

/**
 * Where the API lives.
 *
 * Resolved at runtime from `window.__APP_CONFIG__`, which the nginx container writes into
 * `config.js` when it starts. That means one built image can be deployed to any environment — the
 * alternative, baking the URL in at build time, forces a rebuild per environment and is the usual
 * reason a "production" bundle ends up pointing at staging.
 *
 * Empty string means "same origin", which is how the Docker Compose setup runs: nginx proxies
 * `/api` and `/hubs` to the API container, so the browser makes same-origin requests and there is
 * no CORS involved at all.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => {
    const configured = (globalThis as { __APP_CONFIG__?: { apiBaseUrl?: string } }).__APP_CONFIG__
      ?.apiBaseUrl;

    return (configured ?? '').replace(/\/$/, '');
  },
});
