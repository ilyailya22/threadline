import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';

/** Where a confirmation link lands. Both halves of it come from the query string. */
@Component({
  selector: 'app-confirm-email-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <section class="confirm">
      @switch (state()) {
        @case ('working') {
          <p class="confirm__text">{{ t('list.loading') }}</p>
        }
        @case ('done') {
          <h1 class="confirm__title">{{ t('confirm.done.title') }}</h1>
          <p class="confirm__text">{{ t('confirm.done.text') }}</p>
        }
        @default {
          <h1 class="confirm__title">{{ t('confirm.failed.title') }}</h1>
          <p class="confirm__text">{{ t('confirm.failed.text') }}</p>
        }
      }

      <a class="button button--primary" routerLink="/">{{ t('confirm.back') }}</a>
    </section>
  `,
  styles: `
    .confirm {
      max-width: 28rem;
      margin: 3rem auto;
      padding: 1.75rem;
      border: 1px solid var(--border);
      border-radius: 14px;
      background: var(--surface);
      text-align: center;
      display: grid;
      gap: 0.75rem;
      justify-items: center;
    }

    .confirm__title {
      margin: 0;
      font-size: 1.35rem;
    }

    .confirm__text {
      margin: 0;
      color: var(--text-muted);
    }
  `,
})
export class ConfirmEmailPage {
  private readonly auth = inject(Auth);

  protected readonly t = inject(I18n).t;

  readonly id = input<string>('');
  readonly token = input<string>('');

  protected readonly state = signal<'working' | 'done' | 'failed'>('working');

  constructor() {
    void this.confirm();
  }

  private async confirm(): Promise<void> {
    const id = this.id();
    const token = this.token();

    if (!id || !token) {
      this.state.set('failed');
      return;
    }

    try {
      await this.auth.confirmEmail(id, token);
      this.auth.markConfirmed();
      this.state.set('done');
    } catch {
      this.state.set('failed');
    }
  }
}
