import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';

/** The signed-in person, exactly as `/api/auth/me` describes them. */
export interface Account {
  readonly id: string;
  readonly userName: string;
  readonly email: string;
  readonly homePage?: string | null;
  readonly avatarUrl?: string | null;
  readonly isEmailConfirmed: boolean;
  readonly hasPassword: boolean;
  readonly isGoogleLinked: boolean;
}

/** Which ways in this deployment offers. */
export interface SignInProviders {
  readonly google: boolean;
}

/**
 * Who is signed in, and everything that changes it.
 *
 * The session is a cookie the server sets, so there is no token here to keep, refresh or leak —
 * this holds the account it describes, for the header and the comment form to read.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly current = signal<Account | null>(null);
  private readonly loaded = signal(false);

  readonly account = this.current.asReadonly();
  readonly isSignedIn = computed(() => this.current() !== null);

  /** False until the first `/me` answers, so the header can avoid flashing "Sign in". */
  readonly ready = this.loaded.asReadonly();

  readonly providers = signal<SignInProviders>({ google: false });

  /** Called once at start-up: the cookie may already be there from a previous visit. */
  async restore(): Promise<void> {
    const [account, providers] = await Promise.all([
      firstValueFrom(
        this.http.get<Account | null>(`${this.baseUrl}/api/auth/me`, { withCredentials: true }),
      ).catch(() => null),
      firstValueFrom(this.http.get<SignInProviders>(`${this.baseUrl}/api/auth/providers`)).catch(
        () => ({ google: false }),
      ),
    ]);

    this.current.set(account ?? null);
    this.providers.set(providers);
    this.loaded.set(true);
  }

  async register(email: string, password: string): Promise<Account> {
    const account = await firstValueFrom(
      this.http.post<Account>(
        `${this.baseUrl}/api/auth/register`,
        { email, password },
        { withCredentials: true },
      ),
    );

    this.current.set(account);

    return account;
  }

  async signIn(email: string, password: string): Promise<Account> {
    const account = await firstValueFrom(
      this.http.post<Account>(
        `${this.baseUrl}/api/auth/login`,
        { email, password },
        { withCredentials: true },
      ),
    );

    this.current.set(account);

    return account;
  }

  async signOut(): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.baseUrl}/api/auth/logout`, null, { withCredentials: true }),
    );

    this.current.set(null);
  }

  /** A full page navigation, because the round trip through Google is not an XHR. */
  signInWithGoogle(returnUrl: string): void {
    const target = `${this.baseUrl}/api/auth/google?returnUrl=${encodeURIComponent(returnUrl)}`;

    globalThis.location.assign(target);
  }

  confirmEmail(id: string, token: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(
        `${this.baseUrl}/api/auth/confirm`,
        { id, token },
        { withCredentials: true },
      ),
    );
  }

  resendConfirmation(): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`${this.baseUrl}/api/auth/confirm/resend`, null, {
        withCredentials: true,
      }),
    );
  }

  async updateProfile(userName: string, homePage: string | null): Promise<Account> {
    const account = await firstValueFrom(
      this.http.put<Account>(
        `${this.baseUrl}/api/accounts/me`,
        { userName, homePage },
        { withCredentials: true },
      ),
    );

    this.current.set(account);

    return account;
  }

  async uploadAvatar(file: File): Promise<Account> {
    const form = new FormData();
    form.append('file', file, file.name);

    const account = await firstValueFrom(
      this.http.post<Account>(`${this.baseUrl}/api/accounts/me/avatar`, form, {
        withCredentials: true,
      }),
    );

    this.current.set(account);

    return account;
  }

  async removeAvatar(): Promise<Account> {
    const account = await firstValueFrom(
      this.http.delete<Account>(`${this.baseUrl}/api/accounts/me/avatar`, {
        withCredentials: true,
      }),
    );

    this.current.set(account);

    return account;
  }

  /** Marks the address confirmed locally, so the banner goes away without another round trip. */
  markConfirmed(): void {
    this.current.update((account) => (account ? { ...account, isEmailConfirmed: true } : account));
  }
}
