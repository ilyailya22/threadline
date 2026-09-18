import { Pipe, type PipeTransform } from '@angular/core';

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * Formats a timestamp the way the assignment's screenshot does — `22.05.22 в 22:30` — with a
 * relative label for anything recent, because "3 минуты назад" is what a reader actually wants on
 * a live board.
 */
@Pipe({ name: 'relativeTime' })
export class RelativeTimePipe implements PipeTransform {
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
      return 'только что';
    }

    if (elapsed < HOUR) {
      return `${Math.floor(elapsed / MINUTE)} мин назад`;
    }

    if (elapsed < DAY) {
      return `${Math.floor(elapsed / HOUR)} ч назад`;
    }

    const pad = (n: number) => n.toString().padStart(2, '0');

    return (
      `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${pad(date.getFullYear() % 100)}` +
      ` в ${pad(date.getHours())}:${pad(date.getMinutes())}`
    );
  }
}
