/**
 * Types mirroring the API contract.
 *
 * They are hand-written rather than generated because the surface is small and stable; the one
 * thing that must never drift — the validation rules — is fetched from the server at runtime
 * instead of being duplicated here (see `ValidationRules`).
 */

export type CommentSortField = 'createdAt' | 'userName' | 'email';
export type SortDirection = 'ascending' | 'descending';
export type AttachmentKind = 'Image' | 'TextFile';
export type AttachmentStatus = 'Pending' | 'Ready' | 'Failed';

export interface Author {
  readonly id: string;
  readonly userName: string;
  readonly email: string;
  readonly homePage?: string | null;
}

export interface Attachment {
  readonly id: string;
  readonly kind: AttachmentKind;
  readonly status: AttachmentStatus;
  readonly contentType: string;
  readonly originalFileName: string;
  readonly sizeBytes: number;
  readonly url: string;
  readonly thumbnailUrl?: string | null;
  readonly width?: number | null;
  readonly height?: number | null;
}

export interface CommentListItem {
  readonly id: string;
  readonly author: Author;
  readonly textHtml: string;
  readonly textPreview: string;
  readonly createdAt: string;
  readonly replyCount: number;
  readonly lastReplyAt?: string | null;
  readonly attachments: readonly Attachment[];
}

export interface CommentNode {
  readonly id: string;
  readonly parentId?: string | null;
  readonly rootId: string;
  readonly depth: number;
  readonly author: Author;
  readonly textHtml: string;
  readonly createdAt: string;
  readonly attachments: readonly Attachment[];
  readonly replies: readonly CommentNode[];
}

/** One page of a thread: a flat, depth-first list the client assembles into a tree. */
export interface CommentThread {
  readonly rootId: string;
  readonly totalCount: number;
  readonly nodes: readonly CommentNode[];
  readonly nextCursor?: string | null;
  readonly hasMore: boolean;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

export interface CreateCommentResult {
  readonly id: string;
  readonly rootId: string;
  readonly parentId?: string | null;
  readonly createdAt: string;
  readonly textHtml: string;
}

export interface CommentPreview {
  readonly textHtml: string;
  readonly textPlain: string;
}

export interface CaptchaChallenge {
  readonly id: string;
  readonly imageUrl: string;
  readonly expiresAt: string;
}

export interface FieldRules {
  readonly required: boolean;
  readonly pattern?: string;
  readonly minLength?: number;
  readonly maxLength?: number;
  readonly description?: string;
}

export interface AttachmentRules {
  readonly imageContentTypes: readonly string[];
  readonly imageExtensions: readonly string[];
  readonly maxImageUploadBytes: number;
  readonly maxImageWidth: number;
  readonly maxImageHeight: number;
  readonly textExtensions: readonly string[];
  readonly maxTextFileBytes: number;
}

/**
 * The server's own validation rules, fetched once and used to build the form's validators.
 *
 * The assignment asks for validation on both sides. Fetching the rules instead of re-typing them
 * means the two sides cannot disagree: if the server's pattern for a user name changes, the form
 * picks it up on the next load rather than silently accepting input the server will reject.
 */
export interface ValidationRules {
  readonly userName: FieldRules;
  readonly email: FieldRules;
  readonly homePage: FieldRules;
  readonly text: FieldRules;
  readonly captcha: FieldRules;
  readonly allowedTags: readonly string[];
  readonly allowedAnchorAttributes: readonly string[];
  readonly attachments: AttachmentRules;
  readonly pageSize: number;
}

/** RFC 9457 problem document, as the API's exception handler emits it. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly traceId?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/**
 * What the form reports after a successful post: the server's result plus the author details the
 * user typed. Enough for the page to show the new comment immediately, without waiting for the
 * asynchronous search index to catch up.
 */
export interface CommentPosted {
  readonly result: CreateCommentResult;
  readonly author: {
    readonly userName: string;
    readonly email: string;
    readonly homePage?: string | null;
  };
}
