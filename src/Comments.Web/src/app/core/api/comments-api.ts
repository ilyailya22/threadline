import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import type {
  CaptchaChallenge,
  CommentListItem,
  CommentPreview,
  CommentSortField,
  CommentThread,
  CreateCommentResult,
  PagedResult,
  SortDirection,
  ValidationRules,
} from './models';

export interface TopLevelQuery {
  readonly page: number;
  /** Omitted: the server's default, 25, which is what the assignment fixes. */
  readonly pageSize?: number;
  readonly sortBy: CommentSortField;
  readonly direction: SortDirection;
  readonly search?: string | null;
}

export interface CreateCommentPayload {
  readonly userName: string;
  readonly email: string;
  readonly homePage?: string | null;
  readonly text: string;
  readonly parentId?: string | null;
  readonly captchaId: string;
  readonly captchaAnswer: string;
  readonly file?: File | null;
}

/** Every HTTP call the application makes, in one place. */
@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Validation rules, fetched once per application load.
   *
   * `shareReplay` rather than a plain request: the form, the file picker and the character counter
   * all need these, and they should cost one round trip between them, not three.
   */
  private readonly rules$ = this.http
    .get<ValidationRules>(`${this.baseUrl}/api/validation-rules`)
    .pipe(shareReplay({ bufferSize: 1, refCount: false }));

  getValidationRules(): Observable<ValidationRules> {
    return this.rules$;
  }

  getTopLevel(query: TopLevelQuery): Observable<PagedResult<CommentListItem>> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('sortBy', query.sortBy)
      .set('direction', query.direction);

    if (query.pageSize) {
      params = params.set('pageSize', query.pageSize);
    }

    if (query.search) {
      params = params.set('search', query.search);
    }

    return this.http.get<PagedResult<CommentListItem>>(`${this.baseUrl}/api/comments`, { params });
  }

  /**
   * One page of a thread. Threads are unbounded — a popular one can have thousands of replies — so
   * the server pages them on the materialised path and the client follows `nextCursor`.
   */
  getThread(rootId: string, after?: string | null, limit = 100): Observable<CommentThread> {
    let params = new HttpParams().set('limit', limit);

    if (after) {
      params = params.set('after', after);
    }

    return this.http.get<CommentThread>(`${this.baseUrl}/api/comments/${rootId}/thread`, {
      params,
    });
  }

  /**
   * Renders the preview on the server, with the same sanitiser that will process the real
   * submission — so what the user sees in the preview is exactly what will be stored.
   */
  preview(text: string): Observable<CommentPreview> {
    return this.http.post<CommentPreview>(`${this.baseUrl}/api/comments/preview`, { text });
  }

  /**
   * Requests a CAPTCHA.
   *
   * The image comes back as the response body and the challenge id in a header, so the answer never
   * travels to the browser. The PNG is turned into a blob URL for the `<img>`; the caller is
   * responsible for revoking it.
   */
  issueCaptcha(): Observable<CaptchaChallenge> {
    return this.http
      .get(`${this.baseUrl}/api/captcha`, { observe: 'response', responseType: 'blob' })
      .pipe(
        map((response) => ({
          id: response.headers.get('X-Captcha-Id') ?? '',
          expiresAt: response.headers.get('X-Captcha-Expires-At') ?? '',
          imageUrl: URL.createObjectURL(response.body as Blob),
        })),
      );
  }

  create(payload: CreateCommentPayload): Observable<CreateCommentResult> {
    const form = new FormData();

    form.append('userName', payload.userName);
    form.append('email', payload.email);
    form.append('text', payload.text);
    form.append('captchaId', payload.captchaId);
    form.append('captchaAnswer', payload.captchaAnswer);

    if (payload.homePage) {
      form.append('homePage', payload.homePage);
    }

    if (payload.parentId) {
      form.append('parentId', payload.parentId);
    }

    if (payload.file) {
      form.append('file', payload.file, payload.file.name);
    }

    return this.http.post<CreateCommentResult>(`${this.baseUrl}/api/comments`, form);
  }

  /** Absolute URL for an attachment path returned by the API. */
  absolute(url: string): string {
    return url.startsWith('http') ? url : `${this.baseUrl}${url}`;
  }
}
