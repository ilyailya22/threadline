import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { CommentsApi } from '../../../core/api/comments-api';
import type {
  CommentListItem,
  CommentNode,
  CommentSortField,
  CommentThread,
  PagedResult,
  SortDirection,
} from '../../../core/api/models';
import { CommentsRealtime } from '../../../core/realtime/comments-realtime';
import { RelativeTimePipe } from '../../../shared/relative-time.pipe';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
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

  /** Id of the thread currently expanded inline, and its loaded contents. */
  protected readonly openThreadId = signal<string | null>(null);
  protected readonly thread = signal<CommentThread | null>(null);
  protected readonly threadLoading = signal(false);

  /**
   * Comments that arrived over the socket while the user was looking at page 1.
   *
   * They are held aside rather than spliced into the table, because inserting a row under someone's
   * cursor is how a user clicks the wrong thing. A banner offers to show them instead.
   */
  protected readonly pendingLive = signal<CommentNode[]>([]);

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

    this.realtime.attachmentReady.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      // An attachment finished processing. Reloading whatever is on screen is cheap and is the
      // simplest way to swap the "обрабатывается" placeholder for the real thumbnail.
      this.load();

      if (this.openThreadId()) {
        this.loadThread(this.openThreadId()!);
      }
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
      this.thread.set(null);
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

  protected onCommentCreated(): void {
    this.formOpen.set(false);

    // Back to page 1 of the default sort: with LIFO ordering that is where the new comment is, and
    // leaving the user on page 7 wondering whether it worked is the wrong outcome.
    if (this.page() !== 1 || this.sortBy() !== 'createdAt' || this.direction() !== 'descending') {
      this.navigate({ page: 1, sortBy: 'createdAt', direction: 'descending' });
    } else {
      this.load();
    }
  }

  protected onReplied(rootId: string): void {
    this.loadThread(rootId);
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
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Не удалось загрузить комментарии.');
          this.loading.set(false);
        },
      });
  }

  private loadThread(rootId: string): void {
    this.threadLoading.set(true);

    this.api
      .getThread(rootId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (thread) => {
          this.thread.set(thread);
          this.threadLoading.set(false);
        },
        error: () => {
          this.thread.set(null);
          this.threadLoading.set(false);
        },
      });
  }

  private onLiveComment(comment: CommentNode): void {
    if (comment.parentId) {
      // A reply: only interesting if its thread is open on screen.
      if (this.openThreadId() === comment.rootId) {
        this.loadThread(comment.rootId);
      }

      return;
    }

    const onFirstDefaultPage =
      this.page() === 1 && this.sortBy() === 'createdAt' && this.direction() === 'descending';

    if (!onFirstDefaultPage) {
      return;
    }

    this.pendingLive.update((pending) =>
      pending.some((item) => item.id === comment.id) ? pending : [comment, ...pending],
    );
  }
}
