import { Component, ChangeDetectionStrategy, inject, input } from '@angular/core';

import { CommentsApi } from '../../../core/api/comments-api';
import { I18n } from '../../../core/i18n/i18n';
import type { Attachment } from '../../../core/api/models';
import { LightboxService } from '../../lightbox/lightbox.service';

/** The attachment strip under a comment: a thumbnail for images, a chip for text files. */
@Component({
  selector: 'app-attachment-view',
  templateUrl: './attachment-view.html',
  styleUrl: './attachment-view.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentView {
  private readonly api = inject(CommentsApi);
  private readonly lightbox = inject(LightboxService);

  protected readonly t = inject(I18n).t;

  readonly attachment = input.required<Attachment>();

  protected thumbnailUrl(attachment: Attachment): string {
    return this.api.absolute(attachment.thumbnailUrl ?? attachment.url);
  }

  protected open(attachment: Attachment): void {
    const url = this.api.absolute(attachment.url);

    if (attachment.kind === 'Image') {
      this.lightbox.openImage(url, attachment.originalFileName);
    } else {
      void this.lightbox.openText(url, attachment.originalFileName);
    }
  }

  protected formatSize(bytes: number): string {
    return bytes < 1024
      ? `${bytes} B`
      : bytes < 1024 * 1024
        ? `${Math.round(bytes / 1024)} KB`
        : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
