import { Pipe, inject, type PipeTransform } from '@angular/core';

import { I18n } from '../core/i18n/i18n';

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * Formats a timestamp the way the assignment's screenshot does — a short absolute date — with a
 * relative label for anything recent, because "3 min ago" is what a reader actually wants on a
 * live board.
 *
 * Impure on purpose. Two things change without the input changing: the clock, so "just now" has to
 * become "5 min ago" on the next render, and the language.
 */
@Pipe({ name: 'relativeTime', pure: false })
export class RelativeTimePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: string | Date | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = value instanceof Date ? value : new Date(value);

    if (Number.isNaN(date.getTime())) {
      return '';
    }

    const elapsed = Date.now() - date.getTime();

    if (elapsed < MINUTE) {
      return this.i18n.t('time.now');
    }

    if (elapsed < HOUR) {
      return this.i18n.t('time.minutes', { count: Math.floor(elapsed / MINUTE) });
    }

    if (elapsed < DAY) {
      return this.i18n.t('time.hours', { count: Math.floor(elapsed / HOUR) });
    }

    // Anything older is a date, formatted by the browser for the chosen language rather than by a
    // hand-rolled template that would have to know about each one.
    return date.toLocaleString(this.i18n.tag(), {
      day: '2-digit',
      month: '2-digit',
      year: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}
