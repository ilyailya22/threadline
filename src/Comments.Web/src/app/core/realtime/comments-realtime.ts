import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';

import { API_BASE_URL } from '../config/api-base-url';
import type { Attachment, CommentNode } from '../api/models';

export interface AttachmentReadyEvent {
  readonly commentId: string;
  readonly attachment: Attachment;
}

/**
 * The SignalR connection.
 *
 * Kept as a single long-lived service rather than one connection per component: a WebSocket is an
 * expensive, stateful thing and the page needs exactly one. Components subscribe to the streams
 * below and let the service own the socket's lifecycle.
 */
@Injectable({ providedIn: 'root' })
export class CommentsRealtime {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly destroyRef = inject(DestroyRef);

  private connection?: HubConnection;
  private readonly watchedThreads = new Set<string>();

  private readonly commentCreatedSubject = new Subject<CommentNode>();
  private readonly attachmentReadySubject = new Subject<AttachmentReadyEvent>();

  /** Exposed as a signal so templates can show a "live" indicator without a subscription. */
  readonly connected = signal(false);

  readonly commentCreated = this.commentCreatedSubject.asObservable();
  readonly attachmentReady = this.attachmentReadySubject.asObservable();

  constructor() {
    this.destroyRef.onDestroy(() => void this.connection?.stop());
  }

  async start(): Promise<void> {
    if (this.connection) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${this.baseUrl}/hubs/comments`, { withCredentials: true })

      // Reconnect with a growing backoff and then keep trying every 30 s. A comment board that
      // silently stops updating after a brief network blip is worse than one with no live updates
      // at all, because the user cannot tell.
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('commentCreated', (comment: CommentNode) =>
      this.commentCreatedSubject.next(comment),
    );

    connection.on('attachmentReady', (event: AttachmentReadyEvent) =>
      this.attachmentReadySubject.next(event),
    );

    connection.onreconnected(async () => {
      this.connected.set(true);

      // Group membership lives on the server and is lost with the connection, so every watched
      // thread has to be re-joined — otherwise the page reconnects and still receives nothing.
      for (const rootId of this.watchedThreads) {
        await connection.invoke('WatchThread', rootId);
      }
    });

    connection.onclose(() => this.connected.set(false));

    this.connection = connection;

    try {
      await connection.start();
      this.connected.set(true);
    } catch {
      // Live updates are an enhancement: the page works without them, so a failure here is not
      // surfaced as an error to the user.
      this.connected.set(false);
    }
  }

  async watchThread(rootId: string): Promise<void> {
    this.watchedThreads.add(rootId);

    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('WatchThread', rootId);
    }
  }

  async unwatchThread(rootId: string): Promise<void> {
    this.watchedThreads.delete(rootId);

    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('UnwatchThread', rootId);
    }
  }
}
