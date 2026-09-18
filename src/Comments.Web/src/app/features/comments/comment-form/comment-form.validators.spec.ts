import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';

import { balancedTagsValidator, httpUrlValidator, validatorsFor } from './comment-form.validators';

function errorsOf(value: string, validators: ReturnType<typeof validatorsFor>) {
  return new FormControl(value, validators).errors;
}

describe('validatorsFor', () => {
  const userName = { required: true, pattern: '^[A-Za-z0-9]{2,64}$', minLength: 2, maxLength: 64 };

  it('accepts a value that satisfies every published rule', () => {
    expect(errorsOf('Anonym42', validatorsFor(userName))).toBeNull();
  });

  it('applies the published pattern', () => {
    expect(errorsOf('Анонім', validatorsFor(userName))).toHaveProperty('pattern');
  });

  it('applies required only when the rule asks for it', () => {
    expect(errorsOf('', validatorsFor(userName))).toHaveProperty('required');
    expect(errorsOf('', validatorsFor({ required: false }))).toBeNull();
  });

  it('applies the published length limits', () => {
    expect(
      errorsOf('x'.repeat(65), validatorsFor({ required: true, maxLength: 64 })),
    ).toHaveProperty('maxlength');
  });
});

describe('httpUrlValidator', () => {
  it('treats an empty value as valid, because the field is optional', () => {
    expect(errorsOf('', [httpUrlValidator])).toBeNull();
  });

  it('rejects schemes other than http and https', () => {
    expect(errorsOf('javascript:alert(1)', [httpUrlValidator])).toEqual({ url: true });
  });
});

describe('balancedTagsValidator', () => {
  const validator = balancedTagsValidator(['a', 'code', 'i', 'strong']);

  it('accepts properly nested allowed tags', () => {
    expect(errorsOf('<strong>bold <i>and italic</i></strong>', [validator])).toBeNull();
  });

  it('reports the tag that was left open', () => {
    expect(errorsOf('<strong>bold', [validator])).toEqual({ unbalancedTag: 'strong' });
  });

  it('reports crossed tags', () => {
    expect(errorsOf('<i><strong>x</i></strong>', [validator])).toEqual({ unbalancedTag: 'i' });
  });

  it('ignores tags that are not allowed, since the server escapes them into text', () => {
    expect(errorsOf('<b>not a tag here', [validator])).toBeNull();
  });
});
