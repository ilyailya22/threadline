import { Injectable, signal } from '@angular/core';

export interface LightboxContent {
  readonly kind: 'image' | 'text';
  readonly title: string;
  readonly imageUrl?: string;
  readonly text?: string;
  readonly downloadUrl?: string;
}

/**
 * Owns what the lightbox is showing.
 *
 * The lightbox itself is rendered once, at the root of the application. Any comment at any nesting
 * depth can open it by calling this service — no output events bubbling up through a recursive
 * component tree, and only one `<dialog>` in the DOM no matter how many attachments are on screen.
 */
@Injectable({ providedIn: 'root' })
export class LightboxService {
  private readonly _content = signal<LightboxContent | null>(null);
  private readonly _loading = signal(false);

  readonly content = this._content.asReadonly();
  readonly loading = this._loading.asReadonly();

  openImage(imageUrl: string, title: string): void {
    this._content.set({ kind: 'image', title, imageUrl, downloadUrl: imageUrl });
  }

  /** Opens a text attachment, fetching its contents first. */
  async openText(url: string, title: string): Promise<void> {
    this._loading.set(true);
    this._content.set({ kind: 'text', title, text: '', downloadUrl: url });

    try {
      const response = await fetch(url, { credentials: 'include' });
      const text = await response.text();

      // Held as text and interpolated as text. This is a user-uploaded file; rendering it as
      // markup would hand back exactly the XSS the server took care to prevent.
      this._content.set({ kind: 'text', title, text, downloadUrl: url });
    } catch {
      this._content.set({
        kind: 'text',
        title,
        text: 'Не удалось загрузить файл.',
        downloadUrl: url,
      });
    } finally {
      this._loading.set(false);
    }
  }

  close(): void {
    this._content.set(null);
  }
}
