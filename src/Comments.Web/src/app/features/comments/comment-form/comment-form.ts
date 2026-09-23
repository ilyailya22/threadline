import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators, type AbstractControl } from '@angular/forms';

import { CommentsApi } from '../../../core/api/comments-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';
import type { MessageKey } from '../../../core/i18n/messages';
import type {
  CaptchaChallenge,
  CommentPosted,
  ProblemDetails,
  ValidationRules,
} from '../../../core/api/models';
import { Avatar } from '../../../shared/avatar';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
import { checkAttachment, describeImageSize } from './attachment-check';
import { balancedTagsValidator, httpUrlValidator, validatorsFor } from './comment-form.validators';
import { identityStorage, type Identity } from './identity-storage';

/** A tag button on the markup toolbar. */
interface TagButton {
  readonly label: string;
  readonly open: string;
  readonly close: string;
  readonly title: MessageKey;
}

/** Client-side result of inspecting a chosen file. */
interface SelectedFile {
  readonly file: File;
  readonly previewUrl?: string;
  readonly note?: string;
}

@Component({
  selector: 'app-comment-form',
  templateUrl: './comment-form.html',
  styleUrl: './comment-form.scss',
  imports: [ReactiveFormsModule, SanitizedHtmlPipe, Avatar],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentForm {
  private readonly api = inject(CommentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly t = inject(I18n).t;

  private readonly auth = inject(Auth);

  /**
   * A signed-in account posts as itself: its name and address are on the session, and the CAPTCHA
   * is there to tell a person from a script, which signing in already did.
   */
  protected readonly account = this.auth.account;

  private readonly textAreaRef = viewChild.required<ElementRef<HTMLTextAreaElement>>('textArea');

  /** Set when replying; absent for a new top-level comment. */
  readonly parentId = input<string | null>(null);

  readonly created = output<CommentPosted>();
  readonly cancelled = output<void>();

  /**
   * Rules come from the server, so the client enforces exactly what the server does.
   * `toSignal` with an initial value keeps the template free of `| async` and null checks.
   */
  protected readonly rules = toSignal(this.api.getValidationRules(), { initialValue: null });

  /** What the file picker offers — the server's list, so the picker and the check cannot disagree. */
  protected readonly acceptedExtensions = computed(() => {
    const attachments = this.rules()?.attachments;

    return attachments
      ? [...attachments.imageExtensions, ...attachments.textExtensions].join(',')
      : null;
  });

  protected readonly captcha = signal<CaptchaChallenge | null>(null);
  protected readonly previewHtml = signal<string | null>(null);
  protected readonly selected = signal<SelectedFile | null>(null);
  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);

  protected readonly tagButtons: readonly TagButton[] = [
    { label: 'i', open: '<i>', close: '</i>', title: 'form.tag.italic' },
    { label: 'strong', open: '<strong>', close: '</strong>', title: 'form.tag.bold' },
    { label: 'code', open: '<code>', close: '</code>', title: 'form.tag.code' },
    { label: 'a', open: '<a href="https://" title="">', close: '</a>', title: 'form.tag.link' },
  ];

  /** The allowed tags and the upload limits, both as the server publishes them. */
  protected readonly allowedTags = computed(() => this.rules()?.allowedTags.join(', ') ?? '');

  protected readonly fileHint = computed(() => {
    const attachments = this.rules()?.attachments;

    return attachments
      ? this.t('form.file.hint', {
          maxWidth: attachments.maxImageWidth,
          maxHeight: attachments.maxImageHeight,
          maxTextKb: Math.round(attachments.maxTextFileBytes / 1024),
        })
      : '';
  });

  protected readonly form = this.fb.nonNullable.group({
    // Only the structural checks until the server's rules arrive — see applyRules.
    userName: ['', [Validators.required]],
    email: ['', [Validators.required]],
    homePage: ['', [httpUrlValidator]],
    text: ['', [Validators.required]],
    captchaAnswer: ['', [Validators.required]],
  });

  constructor() {
    if (!this.account()) {
      this.refreshCaptcha();

      // Remembering who you are between comments is the difference between a board people use and
      // one they post to once. Only the identity fields are stored, never the message.
      const remembered = identityStorage.read();

      if (remembered) {
        this.form.patchValue(remembered);
      }
    } else {
      // Nothing to type, nothing to validate.
      for (const name of ['userName', 'email', 'homePage', 'captchaAnswer'] as const) {
        this.form.controls[name].clearValidators();
        this.form.controls[name].updateValueAndValidity({ emitEvent: false });
      }
    }

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.serverError.set(null);
    });

    effect(() => {
      const rules = this.rules();

      if (rules) {
        this.applyRules(rules);
      }
    });

    this.destroyRef.onDestroy(() => this.revokeUrls());
  }

  /** Replaces the provisional validators with the ones built from the server's published rules. */
  private applyRules(rules: ValidationRules): void {
    const { controls } = this.form;

    controls.text.setValidators([
      ...validatorsFor(rules.text),
      balancedTagsValidator(rules.allowedTags),
    ]);

    // The fields an account never fills in have no rules to enforce.
    if (!this.account()) {
      controls.userName.setValidators(validatorsFor(rules.userName));
      controls.email.setValidators(validatorsFor(rules.email));
      controls.homePage.setValidators([...validatorsFor(rules.homePage), httpUrlValidator]);
      controls.captchaAnswer.setValidators(validatorsFor(rules.captcha));
    }

    for (const control of Object.values(controls)) {
      control.updateValueAndValidity({ emitEvent: false });
    }
  }

  protected refreshCaptcha(): void {
    const previous = this.captcha();

    this.api
      .issueCaptcha()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (challenge) => {
          if (previous) {
            URL.revokeObjectURL(previous.imageUrl);
          }

          this.captcha.set(challenge);
          this.form.controls.captchaAnswer.setValue('');
        },
        error: () => this.serverError.set(this.t('error.captcha')),
      });
  }

  /**
   * Wraps the current selection in a tag, or inserts an empty pair at the caret.
   *
   * Selection-aware because that is how people actually use such a toolbar: select a word, press
   * <strong>, get a bold word. Inserting at the caret and making the user type between the tags is
   * the version that gets used once and then ignored.
   */
  protected applyTag(tag: TagButton): void {
    const textArea = this.textAreaRef().nativeElement;
    const { selectionStart, selectionEnd, value } = textArea;

    const selectedText = value.slice(selectionStart, selectionEnd);
    const next = `${value.slice(0, selectionStart)}${tag.open}${selectedText}${tag.close}${value.slice(selectionEnd)}`;

    this.form.controls.text.setValue(next);
    this.previewHtml.set(null);

    // Put the caret between the tags (or after the wrapped text) so typing continues naturally.
    const caret = selectionStart + tag.open.length + selectedText.length;

    queueMicrotask(() => {
      textArea.focus();
      textArea.setSelectionRange(caret, caret);
    });
  }

  /** Server-rendered preview — the same sanitiser that will process the real submission. */
  protected preview(): void {
    const text = this.form.controls.text.value;

    if (!text.trim()) {
      return;
    }

    this.api
      .preview(text)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => this.previewHtml.set(result.textHtml),
        error: (error: HttpErrorResponse) => {
          this.previewHtml.set(null);
          this.applyServerErrors(error);
        },
      });
  }

  protected closePreview(): void {
    this.previewHtml.set(null);
  }

  /** Checks a chosen file in the browser before it is ever uploaded — see `checkAttachment`. */
  protected async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const rules = this.rules()?.attachments;

    this.revokeUrls();
    this.selected.set(null);
    this.fileError.set(null);

    if (!file || !rules) {
      return;
    }

    const check = checkAttachment(file, rules);

    if (!check.ok) {
      this.fileError.set(this.t(check.error, check.params));
      input.value = '';
      return;
    }

    if (check.kind === 'text') {
      this.selected.set({ file });
      return;
    }

    const previewUrl = URL.createObjectURL(file);
    const size = await readImageSize(previewUrl);

    const note = size ? describeImageSize(size, rules) : null;

    this.selected.set({
      file,
      previewUrl,
      note: note ? this.t(note.key, note.params) : undefined,
    });
  }

  protected clearFile(fileInput: HTMLInputElement): void {
    this.revokeUrls();
    this.selected.set(null);
    this.fileError.set(null);
    fileInput.value = '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();

    const me = this.account();
    const challenge = this.captcha();

    if (this.form.invalid || this.submitting() || (!me && !challenge)) {
      return;
    }

    this.submitting.set(true);
    this.serverError.set(null);

    const value = this.form.getRawValue();

    // For an account these are what the server will attach anyway; for a guest they are what was
    // typed. Either way they are what the new row shows until the list reloads.
    const identity: Identity = me
      ? { userName: me.userName, email: me.email, homePage: me.homePage ?? '' }
      : {
          userName: value.userName.trim(),
          email: value.email.trim(),
          homePage: value.homePage.trim(),
        };

    this.api
      .create({
        ...identity,
        homePage: identity.homePage || null,
        text: value.text,
        parentId: this.parentId(),
        captchaId: challenge?.id ?? null,
        captchaAnswer: me ? null : value.captchaAnswer.trim(),
        file: this.selected()?.file ?? null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.submitting.set(false);

          if (!me) {
            identityStorage.write(identity);
          }

          this.resetAfterSubmit();
          this.created.emit({
            result,
            author: { ...identity, homePage: identity.homePage || null },
          });
        },
        error: (error: HttpErrorResponse) => {
          this.submitting.set(false);

          // A CAPTCHA is one-shot, so a failed submission always needs a fresh one — otherwise the
          // user's second attempt fails for a reason that has nothing to do with what they fixed.
          if (!me) {
            this.refreshCaptcha();
          }

          this.applyServerErrors(error);
        },
      });
  }

  protected cancel(): void {
    this.cancelled.emit();
  }

  protected controlError(name: keyof typeof this.form.controls): string | null {
    const control = this.form.controls[name];

    if (!control.touched || control.valid) {
      return null;
    }

    const errors = control.errors ?? {};

    if (errors['server']) {
      return errors['server'] as string;
    }

    if (errors['required']) {
      return this.t('validation.required');
    }

    if (errors['minlength']) {
      return this.t('validation.tooShort');
    }

    if (errors['maxlength']) {
      return this.t('validation.tooLong');
    }

    if (errors['url']) {
      return this.t('validation.url');
    }

    if (errors['unbalancedTag']) {
      return this.t('validation.unbalancedTag', { tag: errors['unbalancedTag'] as string });
    }

    if (errors['pattern']) {
      return name === 'email' ? this.t('validation.email') : this.t('validation.latin');
    }

    return this.t('validation.invalid');
  }

  /** Maps an RFC 9457 validation problem onto the form's controls. */
  private applyServerErrors(error: HttpErrorResponse): void {
    const problem = error.error as ProblemDetails | undefined;

    if (error.status === 429) {
      this.serverError.set(this.t('error.tooManyRequests'));
      return;
    }

    if (!problem?.errors) {
      this.serverError.set(problem?.title ?? this.t('error.submit'));
      return;
    }

    for (const [field, messages] of Object.entries(problem.errors)) {
      const control = (this.form.controls as Record<string, AbstractControl | undefined>)[field];

      if (control) {
        control.setErrors({ server: messages.join(' ') });
        control.markAsTouched();
      } else {
        this.serverError.set(messages.join(' '));
      }
    }
  }

  private resetAfterSubmit(): void {
    const { userName, email, homePage } = this.form.getRawValue();

    this.form.reset({ userName, email, homePage, text: '', captchaAnswer: '' });
    this.previewHtml.set(null);
    this.revokeUrls();
    this.selected.set(null);

    if (!this.account()) {
      this.refreshCaptcha();
    }
  }

  private revokeUrls(): void {
    const previewUrl = this.selected()?.previewUrl;

    if (previewUrl) {
      URL.revokeObjectURL(previewUrl);
    }
  }
}

function readImageSize(url: string): Promise<{ width: number; height: number } | null> {
  return new Promise((resolve) => {
    const image = new Image();

    image.onload = () => resolve({ width: image.naturalWidth, height: image.naturalHeight });
    image.onerror = () => resolve(null);
    image.src = url;
  });
}
