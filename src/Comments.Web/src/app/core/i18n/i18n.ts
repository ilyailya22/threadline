import { Injectable, computed, signal } from '@angular/core';

import { en, uk, type MessageKey, type Messages, type Params } from './messages';

export const LOCALES = ['en', 'uk'] as const;

export type Locale = (typeof LOCALES)[number];

export const LOCALE_NAMES: Readonly<Record<Locale, string>> = {
  en: 'English',
  uk: 'Українська',
};

const STORAGE_KEY = 'threadline.locale';
const DEFAULT_LOCALE: Locale = 'en';

const CATALOGUES: Readonly<Record<Locale, Messages>> = { en, uk };

/**
 * The application's language, and every string it says in it.
 *
 * English is the default and Ukrainian is one click away; the choice is remembered per browser.
 * Switching is instant rather than a reload, which rules out Angular's build-time `$localize` —
 * that ships one bundle per language and changes the URL to change the words.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly current = signal<Locale>(read());

  readonly locale = this.current.asReadonly();

  /**
   * Reads the locale signal on every call, so a template that calls it re-renders when the
   * language changes — no subscriptions, no reload.
   */
  readonly t = (key: MessageKey, params?: Params): string => {
    const message = CATALOGUES[this.current()][key];

    return typeof message === 'string' ? message : message(params ?? {});
  };

  /** Ready-made for `[lang]`, and for anything that formats dates or numbers. */
  readonly tag = computed(() => (this.current() === 'uk' ? 'uk-UA' : 'en-GB'));

  use(locale: Locale): void {
    this.current.set(locale);
    document.documentElement.lang = locale;
    document.title = this.t('app.title');

    try {
      localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      // Private mode or blocked storage: the choice simply does not outlive the tab.
    }
  }
}

/**
 * The remembered choice, then English.
 *
 * Deliberately not the browser's preference: English is the language this is presented in, and a
 * visitor whose browser asks for Ukrainian should still land on the same page everyone else sees.
 * Ukrainian is one click away in the header, and that click is what gets remembered.
 */
function read(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (isLocale(stored)) {
      return stored;
    }
  } catch {
    // Ignored: see use().
  }

  return DEFAULT_LOCALE;
}

function isLocale(value: string | null): value is Locale {
  return value !== null && (LOCALES as readonly string[]).includes(value);
}
