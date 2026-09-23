import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { Avatar } from '../../shared/avatar';

/** Account settings: the nickname, the home page and the picture. */
@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Avatar],
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
})
export class AccountPage {
  private readonly auth = inject(Auth);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly t = inject(I18n).t;

  protected readonly account = this.auth.account;
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly resent = signal(false);

  protected readonly canRemoveAvatar = computed(() => {
    const url = this.account()?.avatarUrl;

    // Only an uploaded one can be removed; Google's picture is not ours to drop.
    return url?.startsWith('/api/accounts/') === true || url?.includes('/api/accounts/') === true;
  });

  protected readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required, Validators.pattern('^[A-Za-z0-9]{2,64}$')]],
    homePage: [''],
  });

  constructor() {
    const account = this.account();

    if (account) {
      this.form.patchValue({ userName: account.userName, homePage: account.homePage ?? '' });
    } else {
      // The cookie is gone or was never there: settings are not a page for a guest.
      void this.router.navigate(['/sign-in'], { queryParams: { returnUrl: '/account' } });
    }
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();

    if (this.form.invalid || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.saved.set(false);

    const { userName, homePage } = this.form.getRawValue();

    try {
      await this.auth.updateProfile(userName.trim(), homePage.trim() || null);
      this.saved.set(true);
    } catch (error) {
      this.error.set(this.messageFor(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async onAvatarSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    input.value = '';

    if (!file) {
      return;
    }

    this.error.set(null);

    try {
      await this.auth.uploadAvatar(file);
    } catch (error) {
      this.error.set(this.messageFor(error));
    }
  }

  protected async removeAvatar(): Promise<void> {
    try {
      await this.auth.removeAvatar();
    } catch (error) {
      this.error.set(this.messageFor(error));
    }
  }

  protected async resendConfirmation(): Promise<void> {
    try {
      await this.auth.resendConfirmation();
      this.resent.set(true);
    } catch (error) {
      this.error.set(this.messageFor(error));
    }
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
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
