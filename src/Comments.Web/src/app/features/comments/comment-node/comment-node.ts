import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

import type { CommentNode, CommentPosted } from '../../../core/api/models';
import { RelativeTimePipe } from '../../../shared/relative-time.pipe';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
import { AttachmentView } from '../attachment-view/attachment-view';
import { CommentForm } from '../comment-form/comment-form';

/**
 * One comment in a thread, and — recursively — everything below it.
 *
 * The recursion is the assignment's "каскадное отображение": a component that renders itself for
 * each reply, to any depth the server sends. Indentation is capped in CSS so that a deep thread
 * stays readable on a phone instead of collapsing into a one-word-per-line column.
 */
@Component({
  selector: 'app-comment-node',
  templateUrl: './comment-node.html',
  styleUrl: './comment-node.scss',
  imports: [AttachmentView, CommentForm, RelativeTimePipe, SanitizedHtmlPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentNodeComponent {
  readonly comment = input.required<CommentNode>();

  /** Raised when a reply is posted anywhere in this subtree, so the page can show it at once. */
  readonly replied = output<CommentPosted>();

  protected readonly replying = signal(false);

  protected toggleReply(): void {
    this.replying.update((value) => !value);
  }

  protected onReplied(posted: CommentPosted): void {
    this.replying.set(false);
    this.replied.emit(posted);
  }
}
