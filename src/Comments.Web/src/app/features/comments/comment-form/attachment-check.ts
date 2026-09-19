import type { AttachmentRules } from '../../../core/api/models';

/** What the browser can tell about a chosen file before uploading it. */
export type AttachmentCheck =
  | { readonly ok: false; readonly error: string }
  | { readonly ok: true; readonly kind: 'image' | 'text' };

/**
 * Checks a chosen file against the rules the server publishes.
 *
 * The server validates again and is the authority — this exists so a person on a slow connection
 * finds out that their 12 MB photo is too large before waiting for the upload, not after.
 */
export function checkAttachment(file: File, rules: AttachmentRules): AttachmentCheck {
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

  if (rules.textExtensions.includes(extension)) {
    return file.size > rules.maxTextFileBytes
      ? {
          ok: false,
          error: `Текстовый файл не должен превышать ${rules.maxTextFileBytes / 1024} КБ.`,
        }
      : { ok: true, kind: 'text' };
  }

  if (rules.imageExtensions.includes(extension)) {
    return file.size > rules.maxImageUploadBytes
      ? {
          ok: false,
          error: `Изображение не должно превышать ${Math.round(rules.maxImageUploadBytes / (1024 * 1024))} МБ.`,
        }
      : { ok: true, kind: 'image' };
  }

  return { ok: false, error: 'Допустимы только JPG, GIF, PNG и TXT.' };
}

/**
 * The size line shown under an image preview. Oversized images are accepted, not rejected — the
 * assignment says they are scaled down — and saying so avoids the surprise of a smaller picture.
 */
export function describeImageSize(
  size: { readonly width: number; readonly height: number },
  rules: AttachmentRules,
): string {
  const oversized = size.width > rules.maxImageWidth || size.height > rules.maxImageHeight;

  return oversized
    ? `${size.width}×${size.height} → будет уменьшено до ${rules.maxImageWidth}×${rules.maxImageHeight}`
    : `${size.width}×${size.height}`;
}
