import type { AttachmentRules } from '../../../core/api/models';
import type { MessageKey, Params } from '../../../core/i18n/messages';

/** What the browser can tell about a chosen file before uploading it. */
export type AttachmentCheck =
  | { readonly ok: false; readonly error: MessageKey; readonly params?: Params }
  | { readonly ok: true; readonly kind: 'image' | 'text' };

/**
 * Checks a chosen file against the rules the server publishes.
 *
 * The server validates again and is the authority — this exists so a person on a slow connection
 * finds out that their 12 MB photo is too large before waiting for the upload, not after. The
 * result names a message rather than carrying one, so the check itself has no language.
 */
export function checkAttachment(file: File, rules: AttachmentRules): AttachmentCheck {
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

  if (rules.textExtensions.includes(extension)) {
    return file.size > rules.maxTextFileBytes
      ? {
          ok: false,
          error: 'file.text.tooLarge',
          params: { maxKb: Math.round(rules.maxTextFileBytes / 1024) },
        }
      : { ok: true, kind: 'text' };
  }

  if (rules.imageExtensions.includes(extension)) {
    return file.size > rules.maxImageUploadBytes
      ? {
          ok: false,
          error: 'file.image.tooLarge',
          params: { maxMb: Math.round(rules.maxImageUploadBytes / (1024 * 1024)) },
        }
      : { ok: true, kind: 'image' };
  }

  return { ok: false, error: 'file.wrongType' };
}

/**
 * The size line shown under an image preview, as a message key and its values.
 *
 * Oversized images are accepted, not rejected — the assignment says they are scaled down — and
 * saying so avoids the surprise of a smaller picture.
 */
export function describeImageSize(
  size: { readonly width: number; readonly height: number },
  rules: AttachmentRules,
): { readonly key: MessageKey; readonly params: Params } {
  const oversized = size.width > rules.maxImageWidth || size.height > rules.maxImageHeight;

  return oversized
    ? {
        key: 'file.willResize',
        params: {
          width: size.width,
          height: size.height,
          maxWidth: rules.maxImageWidth,
          maxHeight: rules.maxImageHeight,
        },
      }
    : { key: 'file.size', params: { width: size.width, height: size.height } };
}
