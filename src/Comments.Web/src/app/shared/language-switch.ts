import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { I18n, LOCALES, LOCALE_NAMES, type Locale } from '../core/i18n/i18n';

/**
 * Two buttons rather than a select: with two languages, a dropdown costs a click and hides the
 * option that is not chosen.
 */
@Component({
  selector: 'app-language-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="language" role="group" [attr.aria-label]="t('app.language')">
      @for (locale of locales; track locale) {
        <button
          type="button"
          class="language__option"
          [class.language__option--active]="i18n.locale() === locale"
          [attr.aria-pressed]="i18n.locale() === locale"
          [attr.lang]="locale"
          [title]="names[locale]"
          (click)="i18n.use(locale)"
        >
          {{ locale.toUpperCase() }}
        </button>
      }
    </div>
  `,
  styles: `
    .language {
      display: inline-flex;
      gap: 0.15rem;
      padding: 0.15rem;
      border: 1px solid var(--border);
      border-radius: 999px;
    }

    .language__option {
      padding: 0.15rem 0.55rem;
      border: 0;
      border-radius: 999px;
      background: transparent;
      color: var(--text-muted);
      font: inherit;
      font-size: 0.72rem;
      font-weight: 600;
      letter-spacing: 0.04em;
      cursor: pointer;
    }

    .language__option:hover {
      color: var(--text);
    }

    .language__option--active {
      background: var(--accent);
      color: var(--accent-contrast);
    }

    .language__option:focus-visible {
      outline: 2px solid var(--accent);
      outline-offset: 2px;
    }
  `,
})
export class LanguageSwitch {
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.t;
  protected readonly locales: readonly Locale[] = LOCALES;
  protected readonly names = LOCALE_NAMES;
}
