import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';

/**
 * One page for both halves of getting in: signing in and signing up differ by a heading, a button
 * and one rule about password length, so they are one component with a mode rather than two pages
 * that drift apart.
 */
@Component({
  selector: 'app-sign-in-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.scss',
})
export class SignInPage {
  private readonly auth = inject(Auth);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly t = inject(I18n).t;

  /** "register" opens the same page with sign-up wording; bound from the route. */
  readonly mode = input<'signIn' | 'register'>('signIn');

  /** Where to go once this works out. Local paths only — the server checks this too. */
  readonly returnUrl = input<string>('/');

  protected readonly providers = this.auth.providers;
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly isRegister = computed(() => this.mode() === 'register');

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();

    try {
      if (this.isRegister()) {
        await this.auth.register(email.trim(), password);
      } else {
        await this.auth.signIn(email.trim(), password);
      }

      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch (error) {
      this.error.set(this.messageFor(error));
    } finally {
      this.submitting.set(false);
    }
  }

  protected continueWithGoogle(): void {
    this.auth.signInWithGoogle(this.safeReturnUrl());
  }

  protected switchMode(): void {
    void this.router.navigate([this.isRegister() ? '/sign-in' : '/register'], {
      queryParams: { returnUrl: this.returnUrl() },
    });
  }

  /** A path on this site, never an absolute URL someone put in the query string. */
  private safeReturnUrl(): string {
    const url = this.returnUrl();

    return url.startsWith('/') && !url.startsWith('//') ? url : '/';
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        return this.t('auth.error.credentials');
      }

      if (error.status === 429) {
        return this.t('error.tooManyRequests');
      }

      const problem = error.error as { errors?: Record<string, string[]>; title?: string };

      if (problem?.errors) {
        return Object.values(problem.errors).flat().join(' ');
      }

      if (problem?.title) {
        return problem.title;
      }
    }

    return this.t('auth.error.generic');
  }
}
