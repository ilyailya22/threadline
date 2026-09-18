import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
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
import type {
  CaptchaChallenge,
  CommentPosted,
  ProblemDetails,
  ValidationRules,
} from '../../../core/api/models';
import { SanitizedHtmlPipe } from '../../../shared/sanitized-html.pipe';
import { balancedTagsValidator, httpUrlValidator, validatorsFor } from './comment-form.validators';

/** A tag button on the markup toolbar. */
interface TagButton {
  readonly label: string;
  readonly open: string;
  readonly close: string;
  readonly title: string;
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
  imports: [ReactiveFormsModule, SanitizedHtmlPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentForm {
  private readonly api = inject(CommentsApi);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

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

  protected readonly captcha = signal<CaptchaChallenge | null>(null);
  protected readonly previewHtml = signal<string | null>(null);
  protected readonly selected = signal<SelectedFile | null>(null);
  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);

  protected readonly tagButtons: readonly TagButton[] = [
    { label: 'i', open: '<i>', close: '</i>', title: 'Курсив' },
    { label: 'strong', open: '<strong>', close: '</strong>', title: 'Полужирный' },
    { label: 'code', open: '<code>', close: '</code>', title: 'Код' },
    { label: 'a', open: '<a href="https://" title="">', close: '</a>', title: 'Ссылка' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    // Only the structural checks until the server's rules arrive — see applyRules.
    userName: ['', [Validators.required]],
    email: ['', [Validators.required]],
    homePage: ['', [httpUrlValidator]],
    text: ['', [Validators.required]],
    captchaAnswer: ['', [Validators.required]],
  });

  constructor() {
    this.refreshCaptcha();

    // Remembering who you are between comments is the difference between a board people use and
    // one they post to once. Only the identity fields are stored, never the message.
    this.restoreIdentity();

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

    controls.userName.setValidators(validatorsFor(rules.userName));
    controls.email.setValidators(validatorsFor(rules.email));
    controls.homePage.setValidators([...validatorsFor(rules.homePage), httpUrlValidator]);
    controls.text.setValidators([
      ...validatorsFor(rules.text),
      balancedTagsValidator(rules.allowedTags),
    ]);
    controls.captchaAnswer.setValidators(validatorsFor(rules.captcha));

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
        error: () => this.serverError.set('Не удалось загрузить CAPTCHA. Попробуйте обновить её.'),
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

  /**
   * Validates a chosen file in the browser before it is ever uploaded.
   *
   * The server validates again and is the authority — this exists so a person on a slow connection
   * finds out that their 12 MB photo is too large before waiting for the upload, not after.
   */
  protected async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    this.revokeUrls();
    this.selected.set(null);
    this.fileError.set(null);

    if (!file) {
      return;
    }

    const rules = this.rules();

    if (!rules) {
      return;
    }

    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    const isImage = rules.attachments.imageExtensions.includes(extension);
    const isText = rules.attachments.textExtensions.includes(extension);

    if (!isImage && !isText) {
      this.fileError.set('Допустимы только JPG, GIF, PNG и TXT.');
      input.value = '';
      return;
    }

    if (isText && file.size > rules.attachments.maxTextFileBytes) {
      this.fileError.set(
        `Текстовый файл не должен превышать ${rules.attachments.maxTextFileBytes / 1024} КБ.`,
      );
      input.value = '';
      return;
    }

    if (isImage && file.size > rules.attachments.maxImageUploadBytes) {
      this.fileError.set(
        `Изображение не должно превышать ${Math.round(rules.attachments.maxImageUploadBytes / (1024 * 1024))} МБ.`,
      );
      input.value = '';
      return;
    }

    if (!isImage) {
      this.selected.set({ file });
      return;
    }

    const previewUrl = URL.createObjectURL(file);
    const size = await readImageSize(previewUrl);

    // Oversized images are accepted, not rejected: the assignment says they must be scaled down.
    // Telling the user it will happen avoids the surprise of a smaller picture than they uploaded.
    const note =
      size &&
      (size.width > rules.attachments.maxImageWidth ||
        size.height > rules.attachments.maxImageHeight)
        ? `${size.width}×${size.height} → будет уменьшено до ${rules.attachments.maxImageWidth}×${rules.attachments.maxImageHeight}`
        : size
          ? `${size.width}×${size.height}`
          : undefined;

    this.selected.set({ file, previewUrl, note });
  }

  protected clearFile(fileInput: HTMLInputElement): void {
    this.revokeUrls();
    this.selected.set(null);
    this.fileError.set(null);
    fileInput.value = '';
  }

  protected submit(): void {
    this.form.markAllAsTouched();

    const challenge = this.captcha();

    if (this.form.invalid || !challenge || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.serverError.set(null);

    const value = this.form.getRawValue();

    this.api
      .create({
        userName: value.userName.trim(),
        email: value.email.trim(),
        homePage: value.homePage.trim() || null,
        text: value.text,
        parentId: this.parentId(),
        captchaId: challenge.id,
        captchaAnswer: value.captchaAnswer.trim(),
        file: this.selected()?.file ?? null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.rememberIdentity();
          this.resetAfterSubmit();
          this.created.emit({
            result,
            author: {
              userName: value.userName.trim(),
              email: value.email.trim(),
              homePage: value.homePage.trim() || null,
            },
          });
        },
        error: (error: HttpErrorResponse) => {
          this.submitting.set(false);

          // A CAPTCHA is one-shot, so a failed submission always needs a fresh one — otherwise the
          // user's second attempt fails for a reason that has nothing to do with what they fixed.
          this.refreshCaptcha();
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
      return 'Обязательное поле.';
    }

    if (errors['minlength']) {
      return 'Слишком короткое значение.';
    }

    if (errors['maxlength']) {
      return 'Слишком длинное значение.';
    }

    if (errors['url']) {
      return 'Укажите абсолютный http/https адрес.';
    }

    if (errors['unbalancedTag']) {
      return `Тег <${errors['unbalancedTag']}> не закрыт или закрыт неправильно.`;
    }

    if (errors['pattern']) {
      return name === 'email' ? 'Некорректный e-mail.' : 'Только латинские буквы и цифры.';
    }

    return 'Некорректное значение.';
  }

  /** Maps an RFC 9457 validation problem onto the form's controls. */
  private applyServerErrors(error: HttpErrorResponse): void {
    const problem = error.error as ProblemDetails | undefined;

    if (error.status === 429) {
      this.serverError.set('Слишком много запросов. Подождите немного и попробуйте снова.');
      return;
    }

    if (!problem?.errors) {
      this.serverError.set(problem?.title ?? 'Не удалось отправить комментарий.');
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
    this.refreshCaptcha();
  }

  private rememberIdentity(): void {
    const { userName, email, homePage } = this.form.getRawValue();

    try {
      localStorage.setItem('dzc.identity', JSON.stringify({ userName, email, homePage }));
    } catch {
      // Private browsing or a full quota. Remembering the name is a convenience, not a feature to
      // fail a submission over.
    }
  }

  private restoreIdentity(): void {
    try {
      const raw = localStorage.getItem('dzc.identity');

      if (raw) {
        this.form.patchValue(JSON.parse(raw) as Partial<typeof this.form.value>);
      }
    } catch {
      // Ignore malformed or unavailable storage.
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
