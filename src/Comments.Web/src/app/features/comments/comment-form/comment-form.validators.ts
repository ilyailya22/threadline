import {
  Validators,
  type AbstractControl,
  type ValidationErrors,
  type ValidatorFn,
} from '@angular/forms';

import type { FieldRules } from '../../../core/api/models';

/**
 * Builds a field's validators from the rules the server publishes at `/api/validation-rules`.
 *
 * The patterns and limits are defined once, in the domain on the server. Writing them again here
 * would be the usual way for client and server validation to drift apart; reading them instead
 * means the two cannot disagree. The server still validates everything — this is for feedback.
 */
export function validatorsFor(rules: FieldRules): ValidatorFn[] {
  const validators: ValidatorFn[] = [];

  if (rules.required) {
    validators.push(Validators.required);
  }

  if (rules.pattern) {
    validators.push(Validators.pattern(new RegExp(rules.pattern)));
  }

  if (rules.minLength !== undefined) {
    validators.push(Validators.minLength(rules.minLength));
  }

  if (rules.maxLength !== undefined) {
    validators.push(Validators.maxLength(rules.maxLength));
  }

  return validators;
}

/** Optional field: empty is valid, anything present must be an absolute http(s) URL. */
export function httpUrlValidator(control: AbstractControl): ValidationErrors | null {
  const value = (control.value as string | null)?.trim();

  if (!value) {
    return null;
  }

  try {
    const url = new URL(value);

    return url.protocol === 'http:' || url.protocol === 'https:' ? null : { url: true };
  } catch {
    return { url: true };
  }
}

/**
 * Checks that the allowed tags are balanced, mirroring the server's rule.
 *
 * The server is the authority and refuses unbalanced markup outright; doing the same check here
 * means the user sees "тег &lt;strong&gt; не закрыт" as they type rather than after a round trip.
 * Unknown tags are ignored on purpose — the server escapes them into text, it does not reject them.
 */
export function balancedTagsValidator(allowedTags: readonly string[]): ValidatorFn {
  const allowed = new Set(allowedTags.map((tag) => tag.toLowerCase()));

  return (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value as string | null) ?? '';
    const stack: string[] = [];

    for (const match of value.matchAll(/<\s*(\/?)\s*([A-Za-z][A-Za-z0-9]*)[^<>]*?>/g)) {
      const [, closing, rawName] = match;
      const name = rawName.toLowerCase();

      if (!allowed.has(name)) {
        continue;
      }

      if (closing) {
        if (stack.pop() !== name) {
          return { unbalancedTag: name };
        }
      } else if (!match[0].endsWith('/>')) {
        stack.push(name);
      }
    }

    return stack.length > 0 ? { unbalancedTag: stack[stack.length - 1] } : null;
  };
}
