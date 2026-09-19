import { describe, expect, it } from 'vitest';

import type { AttachmentRules } from '../../../core/api/models';
import { checkAttachment, describeImageSize } from './attachment-check';

const rules: AttachmentRules = {
  imageContentTypes: ['image/jpeg', 'image/png', 'image/gif'],
  imageExtensions: ['.jpg', '.jpeg', '.png', '.gif'],
  maxImageUploadBytes: 10 * 1024 * 1024,
  maxImageWidth: 320,
  maxImageHeight: 240,
  textExtensions: ['.txt'],
  maxTextFileBytes: 100 * 1024,
};

const fileOf = (name: string, bytes: number) => new File([new Uint8Array(bytes)], name);

describe('checkAttachment', () => {
  it('accepts an image and a text file within their limits', () => {
    expect(checkAttachment(fileOf('photo.JPG', 1024), rules)).toEqual({ ok: true, kind: 'image' });
    expect(checkAttachment(fileOf('notes.txt', 1024), rules)).toEqual({ ok: true, kind: 'text' });
  });

  it('refuses a text file over 100 KB', () => {
    expect(checkAttachment(fileOf('notes.txt', 100 * 1024 + 1), rules).ok).toBe(false);
  });

  it('refuses types the assignment does not allow', () => {
    expect(checkAttachment(fileOf('page.html', 10), rules).ok).toBe(false);
    expect(checkAttachment(fileOf('no-extension', 10), rules).ok).toBe(false);
  });
});

describe('describeImageSize', () => {
  it('says when an image will be scaled down', () => {
    expect(describeImageSize({ width: 640, height: 480 }, rules)).toContain('будет уменьшено');
  });

  it('just states the size of an image that already fits', () => {
    expect(describeImageSize({ width: 320, height: 240 }, rules)).toBe('320×240');
  });
});
