/** The identity fields of the form — the part worth remembering between comments. */
export interface Identity {
  readonly userName: string;
  readonly email: string;
  readonly homePage: string;
}

const KEY = 'dzc.identity';

/**
 * Remembers who the visitor is between comments. Only the identity fields are stored, never the
 * message. Storage can be unavailable (private browsing, a full quota) or hold something malformed;
 * this is a convenience, so any failure is simply ignored.
 */
export const identityStorage = {
  read(): Partial<Identity> | null {
    try {
      const raw = localStorage.getItem(KEY);

      return raw ? (JSON.parse(raw) as Partial<Identity>) : null;
    } catch {
      return null;
    }
  },

  write(identity: Identity): void {
    try {
      localStorage.setItem(KEY, JSON.stringify(identity));
    } catch {
      // Not a reason to fail a submission.
    }
  },
};
