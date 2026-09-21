import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { CommentsApi } from '../../../core/api/comments-api';
import type { AttachmentReadyEvent, CommentNode, CommentPosted } from '../../../core/api/models';
import { CommentsRealtime } from '../../../core/realtime/comments-realtime';
import { buildThreadTree, mergeNodes } from '../../../shared/thread-tree';

/**
 * The thread expanded under a table row: which one is open, its loaded pages, and live additions.
 *
 * Held as the flat list of every node loaded so far — first page, further pages, the user's own
 * replies and replies pushed over the socket — and turned into a tree on demand, so appending never
 * requires re-fetching what is already on screen. Provided per page, so it lives and dies with it.
 */
@Injectable()
export class OpenThread {
  private readonly api = inject(CommentsApi);
  private readonly realtime = inject(CommentsRealtime);
  private readonly destroyRef = inject(DestroyRef);

  private readonly nodes = signal<CommentNode[]>([]);
  private readonly total = signal(0);

  readonly rootId = signal<string | null>(null);
  readonly cursor = signal<string | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);

  readonly tree = computed(() => {
    const rootId = this.rootId();

    return rootId ? buildThreadTree(this.nodes(), rootId) : null;
  });

  readonly remaining = computed(() => Math.max(0, this.total() - this.nodes().length));

  /** Opens the thread, or closes it if it is the one already open. */
  toggle(rootId: string): void {
    const previous = this.rootId();

    if (previous) {
      void this.realtime.unwatchThread(previous);
    }

    if (previous === rootId) {
      this.rootId.set(null);
      this.nodes.set([]);
      return;
    }

    this.rootId.set(rootId);
    this.nodes.set([]);
    this.cursor.set(null);
    void this.realtime.watchThread(rootId);
    this.fetchPage(rootId, null);
  }

  loadMore(): void {
    const rootId = this.rootId();
    const cursor = this.cursor();

    if (rootId && cursor && !this.loading()) {
      this.fetchPage(rootId, cursor);
    }
  }

  /** Shows the user's own reply at once, from what the server returned for it. */
  addOwnReply(posted: CommentPosted): void {
    if (this.rootId() === posted.result.rootId) {
      this.add(toNode(posted, this.nodes()));
    }
  }

  /**
   * A reply pushed over the socket. Added in place rather than by re-fetching, so the pages the
   * reader has expanded stay put; a reply whose parent is not loaded yet is kept and attaches
   * itself when that page arrives.
   */
  addLiveReply(comment: CommentNode): void {
    if (this.rootId() === comment.rootId) {
      this.add(comment);
    }
  }

  /** Swaps the "обрабатывается" placeholder for the processed file. */
  applyAttachmentReady(event: AttachmentReadyEvent): void {
    this.nodes.update((nodes) =>
      nodes.map((node) =>
        node.id === event.commentId
          ? {
              ...node,
              attachments: node.attachments.map((a) =>
                a.id === event.attachment.id ? event.attachment : a,
              ),
            }
          : node,
      ),
    );
  }

  private add(node: CommentNode): void {
    const isNew = !this.nodes().some((existing) => existing.id === node.id);

    if (isNew) {
      this.nodes.update((nodes) => mergeNodes(nodes, [node]));
      this.total.update((total) => total + 1);
    }
  }

  private fetchPage(rootId: string, after: string | null): void {
    this.loading.set(true);
    this.failed.set(false);

    this.api
      .getThread(rootId, after)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          // The reader may have opened a different thread while this page was in flight.
          if (this.rootId() !== rootId) {
            return;
          }

          this.nodes.update((nodes) => mergeNodes(nodes, page.nodes));
          this.total.set(page.totalCount);
          this.cursor.set(page.nextCursor ?? null);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
  }
}

/** The node the server would have returned for a reply the user has just posted. */
function toNode(posted: CommentPosted, existing: readonly CommentNode[]): CommentNode {
  const parent = existing.find((node) => node.id === posted.result.parentId);

  return {
    id: posted.result.id,
    parentId: posted.result.parentId ?? null,
    rootId: posted.result.rootId,
    depth: (parent?.depth ?? 0) + 1,
    author: { id: '', ...posted.author },
    textHtml: posted.result.textHtml,
    createdAt: posted.result.createdAt,
    attachments: posted.result.attachments,
  };
}
