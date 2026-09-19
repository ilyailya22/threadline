import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, type ParamMap } from '@angular/router';

import { CommentsApi } from '../../../core/api/comments-api';
import type {
  CommentListItem,
  CommentNode,
  CommentPosted,
  CommentSortField,
  PagedResult,
  SortDirection,
} from '../../../core/api/models';
import { CommentsRealtime } from '../../../core/realtime/comments-realtime';
import { RelativeTimePipe } from '../../../shared/relative-time.pipe';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
import { AttachmentView } from '../attachment-view/attachment-view';
import { CommentForm } from '../comment-form/comment-form';
import { CommentNodeComponent } from '../comment-node/comment-node';
import { OpenThread } from './open-thread';

interface SortColumn {
  readonly field: CommentSortField;
  readonly label: string;
}

const SORT_FIELDS: readonly CommentSortField[] = ['userName', 'email', 'createdAt'];
const DIRECTIONS: readonly SortDirection[] = ['ascending', 'descending'];

/** The table's state as the URL holds it. */
interface ListState {
  readonly page: number;
  readonly sortBy: CommentSortField;
  readonly direction: SortDirection;
}

/** LIFO by default, exactly as the assignment specifies. */
const DEFAULT_STATE: ListState = { page: 1, sortBy: 'createdAt', direction: 'descending' };

@Component({
  selector: 'app-comments-page',
  templateUrl: './comments-page.html',
  styleUrl: './comments-page.scss',
  imports: [AttachmentView, CommentForm, CommentNodeComponent, RelativeTimePipe, SanitizedHtmlPipe],
  providers: [OpenThread],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentsPage implements OnInit {
  private readonly api = inject(CommentsApi);
  private readonly realtime = inject(CommentsRealtime);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly thread = inject(OpenThread);

  protected readonly columns: readonly SortColumn[] = [
    { field: 'userName', label: 'User Name' },
    { field: 'email', label: 'E-mail' },
    { field: 'createdAt', label: 'Дата добавления' },
  ];

  protected readonly page = signal(DEFAULT_STATE.page);
  protected readonly sortBy = signal(DEFAULT_STATE.sortBy);
  protected readonly direction = signal(DEFAULT_STATE.direction);

  protected readonly result = signal<PagedResult<CommentListItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);

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
      const state = readListState(params);

      this.page.set(state.page);
      this.sortBy.set(state.sortBy);
      this.direction.set(state.direction);

      this.load();
    });

    void this.realtime.start();

    this.realtime.commentCreated
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((comment) => this.onLiveComment(comment));

    this.realtime.attachmentReady.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      // The table is reloaded (it is one cached request); the open thread is patched in place, so
      // pages the reader has already expanded are not thrown away.
      this.load();
      this.thread.applyAttachmentReady(event);
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

  protected onCommentCreated(posted: CommentPosted): void {
    this.formOpen.set(false);
    this.ownComments.add(posted.result.id);
    this.unconfirmedOwn.set(posted.result.id, posted);

    // Back to page 1 of the default sort: with LIFO ordering that is where the new comment is, and
    // leaving the user on page 7 wondering whether it worked is the wrong outcome. The reload that
    // navigation triggers inserts it via reconcileOwnComments.
    if (!this.onFirstDefaultPage()) {
      this.navigate(DEFAULT_STATE);
      return;
    }

    this.insertOptimistically(posted);
  }

  /** Shows the user's own reply at once, the same way the table shows their own top-level comment. */
  protected onReplied(posted: CommentPosted): void {
    this.ownComments.add(posted.result.id);
    this.thread.addOwnReply(posted);

    // The reply count in the table changes too.
    this.load();
  }

  protected showPendingLive(): void {
    this.pendingLive.set([]);
    this.load();
  }

  private onFirstDefaultPage(): boolean {
    return (
      this.page() === DEFAULT_STATE.page &&
      this.sortBy() === DEFAULT_STATE.sortBy &&
      this.direction() === DEFAULT_STATE.direction
    );
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
      items: [item, ...current.items].slice(0, current.pageSize),
      totalCount: current.totalCount + 1,
    });
  }

  private navigate(changes: Partial<ListState>): void {
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
      .getTopLevel({ page: this.page(), sortBy: this.sortBy(), direction: this.direction() })
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

  private onLiveComment(comment: CommentNode): void {
    if (comment.parentId) {
      this.thread.addLiveReply(comment);
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

/**
 * Reads the table state from the query string. The URL is user input — hand-edited, bookmarked
 * from an older version — so anything unrecognised falls back to the default rather than reaching
 * the API as a guaranteed 400.
 */
function readListState(params: ParamMap): ListState {
  const page = Number(params.get('page'));
  const sortBy = params.get('sortBy') as CommentSortField | null;
  const direction = params.get('direction') as SortDirection | null;

  return {
    page: Number.isInteger(page) && page >= 1 ? page : DEFAULT_STATE.page,
    sortBy: sortBy && SORT_FIELDS.includes(sortBy) ? sortBy : DEFAULT_STATE.sortBy,
    direction: direction && DIRECTIONS.includes(direction) ? direction : DEFAULT_STATE.direction,
  };
}
