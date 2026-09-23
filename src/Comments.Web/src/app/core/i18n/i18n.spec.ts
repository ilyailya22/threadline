import { TestBed } from '@angular/core/testing';

import { I18n } from './i18n';
import { en, uk } from './messages';

describe('I18n', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('starts in English and switches on request', () => {
    const i18n = TestBed.inject(I18n);

    expect(i18n.locale()).toBe('en');
    expect(i18n.t('list.title')).toBe('Comments');

    i18n.use('uk');

    expect(i18n.t('list.title')).toBe('Коментарі');
    expect(document.documentElement.lang).toBe('uk');
  });

  it('remembers the choice for the next visit', () => {
    TestBed.inject(I18n).use('uk');
    TestBed.resetTestingModule();

    expect(TestBed.inject(I18n).locale()).toBe('uk');
  });

  it('interpolates values into a message', () => {
    const i18n = TestBed.inject(I18n);

    expect(i18n.t('thread.more', { count: 42 })).toBe('Show more (42)');

    i18n.use('uk');

    expect(i18n.t('thread.more', { count: 42 })).toBe('Показати ще (42)');
  });

  /**
   * The types already force the Ukrainian catalogue to carry every key. This checks the other
   * half — that no key was left with its English text copied across as a placeholder.
   */
  it('translates every message', () => {
    const untranslated = Object.keys(en).filter((key) => {
      const english = en[key as keyof typeof en];
      const ukrainian = uk[key as keyof typeof uk];

      return (
        typeof english === 'string' &&
        typeof ukrainian === 'string' &&
        english === ukrainian &&
        // These are the same word in both languages, or not words at all.
        !['app.stack', 'column.email', 'form.email', 'form.captcha'].includes(key)
      );
    });

    expect(untranslated).toEqual([]);
  });
});
