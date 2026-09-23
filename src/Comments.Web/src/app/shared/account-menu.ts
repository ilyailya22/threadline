import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { Auth } from '../core/auth/auth';
import { I18n } from '../core/i18n/i18n';
import { Avatar } from './avatar';

/** The right-hand end of the header: sign in, or who you are and the way out. */
@Component({
  selector: 'app-account-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Avatar],
  template: `
    @if (auth.ready()) {
      @if (auth.account(); as me) {
        <div class="menu">
          <button
            type="button"
            class="menu__trigger"
            [attr.aria-expanded]="open()"
            (click)="open.set(!open())"
          >
            <app-avatar [name]="me.userName" [url]="me.avatarUrl" />
            <span class="menu__name">{{ me.userName }}</span>
          </button>

          @if (open()) {
            <!-- Closed by a click anywhere else. A button rather than a div, so the same thing
                 works from the keyboard and screen readers are told what it does. -->
            <button
              type="button"
              class="menu__backdrop"
              [attr.aria-label]="t('lightbox.close')"
              (click)="open.set(false)"
            ></button>

            <div class="menu__panel" role="menu">
              <p class="menu__email">{{ me.email }}</p>

              @if (!me.isEmailConfirmed) {
                <p class="menu__unconfirmed">{{ t('account.unconfirmed.short') }}</p>
              }

              <a class="menu__item" routerLink="/account" (click)="open.set(false)">
                {{ t('account.settings') }}
              </a>

              <button type="button" class="menu__item" (click)="signOut()">
                {{ t('auth.signOut') }}
              </button>
            </div>
          }
        </div>
      } @else {
        <a class="menu__signin" routerLink="/sign-in" [queryParams]="{ returnUrl: currentUrl() }">
          {{ t('auth.signIn.action') }}
        </a>
      }
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    .menu {
      position: relative;
    }

    .menu__trigger {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.2rem 0.5rem 0.2rem 0.2rem;
      border: 1px solid var(--border);
      border-radius: 999px;
      background: transparent;
      color: var(--text);
      font: inherit;
      font-size: 0.82rem;
      cursor: pointer;
    }

    .menu__trigger:hover {
      border-color: var(--accent);
    }

    .menu__name {
      max-width: 10rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .menu__backdrop {
      position: fixed;
      inset: 0;
      z-index: 20;
      border: 0;
      background: transparent;
      cursor: default;
    }

    .menu__panel {
      position: absolute;
      right: 0;
      top: calc(100% + 0.4rem);
      z-index: 21;
      min-width: 13rem;
      display: grid;
      padding: 0.4rem;
      border: 1px solid var(--border);
      border-radius: 12px;
      background: var(--surface);
      box-shadow: 0 12px 30px rgb(0 0 0 / 35%);
    }

    .menu__email {
      margin: 0;
      padding: 0.4rem 0.6rem;
      color: var(--text-muted);
      font-size: 0.78rem;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .menu__unconfirmed {
      margin: 0 0 0.2rem;
      padding: 0 0.6rem 0.4rem;
      color: var(--accent);
      font-size: 0.75rem;
    }

    .menu__item {
      padding: 0.5rem 0.6rem;
      border: 0;
      border-radius: 8px;
      background: none;
      color: var(--text);
      font: inherit;
      font-size: 0.85rem;
      text-align: left;
      text-decoration: none;
      cursor: pointer;
    }

    .menu__item:hover {
      background: var(--surface-raised, rgb(255 255 255 / 6%));
    }

    /* An outlined button, not a word. This is the only way into an account, and set in plain text
       beside the language switch it read as a caption — people looked straight past it. */
    .menu__signin {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      padding: 0.35rem 0.85rem;
      border: 1px solid var(--accent);
      border-radius: 999px;
      color: var(--accent);
      font-size: 0.82rem;
      font-weight: 600;
      text-decoration: none;
      white-space: nowrap;
    }

    .menu__signin:hover,
    .menu__signin:focus-visible {
      background: var(--accent);
      color: var(--accent-contrast, #fff);
    }
  `,
})
export class AccountMenu {
  private readonly router = inject(Router);

  protected readonly auth = inject(Auth);
  protected readonly t = inject(I18n).t;
  protected readonly open = signal(false);

  protected currentUrl(): string {
    return this.router.url || '/';
  }

  protected async signOut(): Promise<void> {
    this.open.set(false);

    await this.auth.signOut();
    await this.router.navigateByUrl('/');
  }
}
