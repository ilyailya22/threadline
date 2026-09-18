import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { CommentsApi } from '../../../core/api/comments-api';
import type {
  CommentListItem,
  CommentPosted,
  CommentNode,
  CommentSortField,
  PagedResult,
  SortDirection,
} from '../../../core/api/models';
import { CommentsRealtime } from '../../../core/realtime/comments-realtime';
import { RelativeTimePipe } from '../../../shared/relative-time.pipe';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
import { buildThreadTree, mergeNodes } from '../../../shared/thread-tree';
import { AttachmentView } from '../attachment-view/attachment-view';
import { CommentForm } from '../comment-form/comment-form';
import { CommentNodeComponent } from '../comment-node/comment-node';

const PAGE_SIZE = 25;

interface SortColumn {
  readonly field: CommentSortField;
  readonly label: string;
}

@Component({
  selector: 'app-comments-page',
  templateUrl: './comments-page.html',
  styleUrl: './comments-page.scss',
  imports: [AttachmentView, CommentForm, CommentNodeComponent, RelativeTimePipe, SanitizedHtmlPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentsPage implements OnInit {
  private readonly api = inject(CommentsApi);
  private readonly realtime = inject(CommentsRealtime);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns: readonly SortColumn[] = [
    { field: 'userName', label: 'User Name' },
    { field: 'email', label: 'E-mail' },
    { field: 'createdAt', label: 'Дата добавления' },
  ];

  protected readonly page = signal(1);
  protected readonly sortBy = signal<CommentSortField>('createdAt');

  /** LIFO by default, exactly as the assignment specifies. */
  protected readonly direction = signal<SortDirection>('descending');

  protected readonly result = signal<PagedResult<CommentListItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);

  /**
   * The thread expanded inline. Held as the flat list of every node loaded so far — first page,
   * further pages, and live additions — and turned into a tree on demand, so appending never
   * requires re-fetching what is already on screen.
   */
  protected readonly openThreadId = signal<string | null>(null);
  protected readonly threadNodes = signal<CommentNode[]>([]);
  protected readonly threadTotal = signal(0);
  protected readonly threadCursor = signal<string | null>(null);
  protected readonly threadLoading = signal(false);
  protected readonly threadError = signal(false);

  protected readonly threadTree = computed(() => {
    const rootId = this.openThreadId();

    return rootId ? buildThreadTree(this.threadNodes(), rootId) : null;
  });

  protected readonly threadRemaining = computed(() =>
    Math.max(0, this.threadTotal() - this.threadNodes().length),
  );

  /**
   * Comments that arrived over the socket while the user was looking at page 1.
   *
   * They are held aside rather than spliced into the table, because inserting a row under someone's
   * cursor is how a user clicks the wrong thing. A banner offers to show them instead.
   */
  protected readonly pendingLive = signal<CommentNode[]>([]);

  /** Ids this browser posted, so their SignalR echo is not offered back as "new". */
  private readonly ownComments = new Set<string>();

  /**
   * This browser's own posts that the search index may not have caught up with yet. Every list load
   * re-inserts the ones it does not contain, and forgets each one the moment the index returns it —
   * so an unrelated reload in the meantime cannot make a just-posted comment vanish.
   */
  private readonly unconfirmedOwn = new Map<string, CommentPosted>();

  protected readonly liveConnected = this.realtime.connected;

  protected readonly pageNumbers = computed(() => {
    const total = this.result()?.totalPages ?? 0;
    const current = this.page();

    if (total <= 7) {
      return Array.from({ length: total }, (_, index) => index + 1);
    }

    // A window around the current page, always including the first and last.
    const window = new Set<number>([1, total, current]);

    for (let offset = 1; offset <= 2; offset++) {
      if (current - offset > 1) {
        window.add(current - offset);
      }

      if (current + offset < total) {
        window.add(current + offset);
      }
    }

    return [...window].sort((a, b) => a - b);
  });

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.page.set(Math.max(1, Number(params.get('page') ?? 1) || 1));
      this.sortBy.set((params.get('sortBy') as CommentSortField | null) ?? 'createdAt');
      this.direction.set((params.get('direction') as SortDirection | null) ?? 'descending');

      this.load();
    });

    void this.realtime.start();

    this.realtime.commentCreated.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((comment) => {
      this.onLiveComment(comment);
    });

    this.realtime.attachmentReady.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      // Swap the "обрабатывается" placeholder for the real file in place. The table is reloaded (it is
      // one cached request); the open thread is patched rather than re-fetched, so pages the reader
      // has already expanded are not thrown away.
      this.load();

      this.threadNodes.update((nodes) =>
        nodes.map((node) =>
          node.id === event.commentId
            ? {
                ...node,
                attachments: node.attachments.map((a) => (a.id === event.attachment.id ? event.attachment : a)),
              }
            : node,
        ),
      );
    });
  }

  protected sort(field: CommentSortField): void {
    // Clicking the active column flips the direction; a new column starts descending, which for a
    // date means newest first and for a name means the user sees an obvious change.
    const direction: SortDirection =
      this.sortBy() === field && this.direction() === 'descending' ? 'ascending' : 'descending';

    this.navigate({ page: 1, sortBy: field, direction });
  }

  protected goToPage(page: number): void {
    this.navigate({ page });
  }

  protected toggleThread(id: string): void {
    if (this.openThreadId() === id) {
      void this.realtime.unwatchThread(id);
      this.openThreadId.set(null);
      this.threadNodes.set([]);
      return;
    }

    const previous = this.openThreadId();

    if (previous) {
      void this.realtime.unwatchThread(previous);
    }

    this.openThreadId.set(id);
    void this.realtime.watchThread(id);
    this.loadThread(id);
  }

  protected onCommentCreated(posted: CommentPosted): void {
    this.formOpen.set(false);
    this.ownComments.add(posted.result.id);
    this.unconfirmedOwn.set(posted.result.id, posted);

    // Back to page 1 of the default sort: with LIFO ordering that is where the new comment is, and
    // leaving the user on page 7 wondering whether it worked is the wrong outcome. The reload that
    // navigation triggers inserts it via reconcileOwnComments.
    if (!this.onFirstDefaultPage()) {
      this.navigate({ page: 1, sortBy: 'createdAt', direction: 'descending' });
      return;
    }

    this.insertOptimistically(posted);
  }

  private onFirstDefaultPage(): boolean {
    return this.page() === 1 && this.sortBy() === 'createdAt' && this.direction() === 'descending';
  }

  private reconcileOwnComments(): void {
    const current = this.result();

    if (!current || this.unconfirmedOwn.size === 0) {
      return;
    }

    for (const [id, posted] of this.unconfirmedOwn) {
      if (current.items.some((item) => item.id === id)) {
        this.unconfirmedOwn.delete(id);
      } else if (this.onFirstDefaultPage()) {
        this.insertOptimistically(posted);
      }
    }
  }

  /**
   * Shows the user's own comment at once.
   *
   * The list is served from a search index that the worker updates asynchronously, so re-reading it
   * straight after posting usually returns the page without the new comment — which, to the person
   * who just pressed "Send", looks exactly like the post failed. The server has already told us what
   * was stored, so it is rendered from that and the next real load reconciles.
   */
  private insertOptimistically(posted: CommentPosted): void {
    const current = this.result();

    if (!current || current.items.some((item) => item.id === posted.result.id)) {
      return;
    }

    const item: CommentListItem = {
      id: posted.result.id,
      author: { id: '', ...posted.author },
      textHtml: posted.result.textHtml,
      textPreview: '',
      createdAt: posted.result.createdAt,
      replyCount: 0,
      attachments: [],
    };

    this.result.set({
      ...current,
      items: [item, ...current.items].slice(0, PAGE_SIZE),
      totalCount: current.totalCount + 1,
    });
  }

  /** Shows the user's own reply at once, the same way the table shows their own top-level comment. */
  protected onReplied(posted: CommentPosted): void {
    this.ownComments.add(posted.result.id);

    if (this.openThreadId() === posted.result.rootId) {
      this.threadNodes.update((nodes) => mergeNodes(nodes, [toNode(posted, nodes)]));
      this.threadTotal.update((total) => total + 1);
    }

    // The reply count in the table changes too.
    this.load();
  }

  protected showPendingLive(): void {
    this.pendingLive.set([]);
    this.load();
  }

  protected trackById(_: number, item: { id: string }): string {
    return item.id;
  }

  private navigate(changes: {
    page?: number;
    sortBy?: CommentSortField;
    direction?: SortDirection;
  }): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        page: changes.page ?? this.page(),
        sortBy: changes.sortBy ?? this.sortBy(),
        direction: changes.direction ?? this.direction(),
      },

      // The list state lives in the URL, so a sorted page can be shared, bookmarked and reached
      // with the back button — none of which works if it lives only in component state.
      queryParamsHandling: 'merge',
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api
      .getTopLevel({
        page: this.page(),
        pageSize: PAGE_SIZE,
        sortBy: this.sortBy(),
        direction: this.direction(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.reconcileOwnComments();
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Не удалось загрузить комментарии.');
          this.loading.set(false);
        },
      });
  }

  /** Loads the first page of a thread, replacing whatever thread was open. */
  private loadThread(rootId: string): void {
    this.threadNodes.set([]);
    this.threadCursor.set(null);
    this.fetchThreadPage(rootId, null);
  }

  /** Appends the next page of the open thread. */
  protected loadMoreThread(): void {
    const rootId = this.openThreadId();
    const cursor = this.threadCursor();

    if (rootId && cursor && !this.threadLoading()) {
      this.fetchThreadPage(rootId, cursor);
    }
  }

  private fetchThreadPage(rootId: string, after: string | null): void {
    this.threadLoading.set(true);
    this.threadError.set(false);

    this.api
      .getThread(rootId, after)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          // The reader may have opened a different thread while this page was in flight.
          if (this.openThreadId() !== rootId) {
            return;
          }

          this.threadNodes.update((nodes) => mergeNodes(nodes, page.nodes));
          this.threadTotal.set(page.totalCount);
          this.threadCursor.set(page.nextCursor ?? null);
          this.threadLoading.set(false);
        },
        error: () => {
          this.threadError.set(true);
          this.threadLoading.set(false);
        },
      });
  }

  private onLiveComment(comment: CommentNode): void {
    if (comment.parentId) {
      // A reply: only interesting if its thread is open on screen. Added in place rather than by
      // re-fetching, so the pages the reader has expanded stay put; a reply whose parent has not
      // been loaded yet is kept too, and attaches itself when that page arrives.
      if (this.openThreadId() === comment.rootId) {
        const isNew = !this.threadNodes().some((node) => node.id === comment.id);

        this.threadNodes.update((nodes) => mergeNodes(nodes, [comment]));

        if (isNew) {
          this.threadTotal.update((total) => total + 1);
        }
      }

      return;
    }

    // The socket echoes the user's own comment back too; it is already on screen, so no banner.
    if (!this.onFirstDefaultPage() || this.ownComments.has(comment.id)) {
      return;
    }

    this.pendingLive.update((pending) =>
      pending.some((item) => item.id === comment.id) ? pending : [comment, ...pending],
    );
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
    attachments: [],
    replies: [],
  };
}
