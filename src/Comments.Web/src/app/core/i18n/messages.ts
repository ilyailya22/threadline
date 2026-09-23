/**
 * Every piece of text the application shows, in both languages it speaks.
 *
 * One file rather than one per language: a missing translation is then a compile error instead of
 * something a reader discovers, because `Messages` is derived from the English set and the
 * Ukrainian one has to match it key for key.
 */
export const en = {
  'app.skipToContent': 'Skip to content',
  'app.tagline': 'SPA application: Comments',
  'app.stack': '.NET 10 · Angular 22 · MS SQL · RabbitMQ · Elasticsearch · Redis · Azure',
  'app.language': 'Language',
  'app.title': 'Comments — Threadline',

  'list.title': 'Comments',
  'list.subtitle': 'Top-level comments, 25 per page. Newest first by default.',
  'list.caption': 'Top-level comments, sortable by name, e-mail and date',
  'list.live.on': 'Live updates',
  'list.live.off': 'Disconnected',
  'list.live.label': 'Live',
  'list.new': 'New comment',
  'list.collapse': 'Collapse',
  'list.pending': ({ count }: Params) => `New comments: ${count} — show`,
  'list.loading': 'Loading…',
  'list.empty': 'No comments yet. Be the first.',
  'list.error': 'Could not load the comments.',
  'list.total': ({ count }: Params) => `Top-level comments: ${count}`,
  'list.page': ({ page, pages }: Params) => `page ${page} of ${pages}`,
  'list.pageCap': ({ pages }: Params) =>
    `only the first ${pages} pages can be paged through — narrow the list by sorting`,

  'column.userName': 'User Name',
  'column.email': 'E-mail',
  'column.date': 'Date added',
  'column.comment': 'Comment',
  'column.replies': 'Replies',
  'column.sortBy': ({ column }: Params) => `Sort by ${column}`,

  'paging.label': 'Comment pages',
  'paging.previous': '← Back',
  'paging.next': 'Next →',
  'paging.goTo': ({ page }: Params) => `Page ${page}`,

  'thread.loading': 'Loading the thread…',
  'thread.error': 'Could not load the thread.',
  'thread.more': ({ count }: Params) => `Show more (${count})`,
  'thread.reply': 'Reply',
  'thread.cancel': 'Cancel',

  'attachment.open': ({ name }: Params) => `Open image ${name}`,
  'attachment.openText': ({ name }: Params) => `Open text file ${name}`,

  'lightbox.download': 'Download',
  'lightbox.close': 'Close',
  'lightbox.loading': 'Loading…',
  'lightbox.loadFailed': 'Could not load the file.',

  'form.userName': 'User Name',
  'form.email': 'E-mail',
  'form.homePage': 'Home page',
  'form.homePage.hint': 'Optional.',
  'form.text': 'Text',
  'form.file': 'File',
  'form.captcha': 'CAPTCHA',
  'form.captcha.placeholder': 'Characters from the picture',
  'form.captcha.refresh': 'New picture',
  'form.captcha.alt': 'CAPTCHA: the characters to type',
  'form.preview': 'Preview',
  'form.preview.title': 'PREVIEW',
  'form.preview.close': 'Close the preview',
  'form.submit': 'Send',
  'form.submitting': 'Sending…',
  'form.cancel': 'Cancel',
  'form.tags.hint': ({ tags }: Params) => `Allowed tags: ${tags}. Every tag must be closed.`,
  'form.file.hint': ({ maxWidth, maxHeight, maxTextKb }: Params) =>
    `JPG, GIF, PNG (scaled down to ${maxWidth}×${maxHeight}) or TXT up to ${maxTextKb} KB.`,
  'form.file.clear': 'Remove',
  'form.file.preview': 'Preview of the chosen image',
  'form.tags.group': 'Allowed HTML tags',
  'form.counter': ({ length, max }: Params) => `${length} / ${max}`,

  'form.tag.italic': 'Italic',
  'form.tag.bold': 'Bold',
  'form.tag.code': 'Code',
  'form.tag.link': 'Link',

  'validation.required': 'Required field.',
  'validation.tooShort': 'Too short.',
  'validation.tooLong': 'Too long.',
  'validation.url': 'Enter an absolute http/https address.',
  'validation.email': 'Invalid e-mail.',
  'validation.latin': 'Latin letters and digits only.',
  'validation.unbalancedTag': ({ tag }: Params) => `Tag <${tag}> is not closed, or closed wrongly.`,
  'validation.invalid': 'Invalid value.',

  'error.captcha': 'Could not load the CAPTCHA. Try refreshing it.',
  'error.submit': 'Could not post the comment.',
  'error.tooManyRequests': 'Too many requests. Wait a moment and try again.',

  'file.image.tooLarge': ({ maxMb }: Params) => `An image must not exceed ${maxMb} MB.`,
  'file.text.tooLarge': ({ maxKb }: Params) => `A text file must not exceed ${maxKb} KB.`,
  'file.wrongType': 'Only JPG, GIF, PNG and TXT are allowed.',
  'file.size': ({ width, height }: Params) => `${width}×${height}`,
  'file.willResize': ({ width, height, maxWidth, maxHeight }: Params) =>
    `${width}×${height} → will be scaled down to ${maxWidth}×${maxHeight}`,

  'auth.signIn.title': 'Sign in',
  'auth.signIn.subtitle': 'To post as yourself, with your name and picture.',
  'auth.signIn.action': 'Sign in',
  'auth.register.title': 'Create an account',
  'auth.register.subtitle':
    'An address and a password. The nickname comes from the address and can be changed later.',
  'auth.register.action': 'Create account',
  'auth.google': 'Continue with Google',
  'auth.or': 'or',
  'auth.password': 'Password',
  'auth.password.hint': ({ min }: Params) => `At least ${min} characters. Length is the only rule.`,
  'auth.working': 'One moment…',
  'auth.signOut': 'Sign out',
  'auth.noAccount': 'No account yet?',
  'auth.haveAccount': 'Already have an account?',
  'auth.error.credentials': 'E-mail or password is incorrect.',
  'auth.error.generic': 'Something went wrong. Try again.',

  'account.title': 'Account',
  'account.settings': 'Account settings',
  'account.save': 'Save',
  'account.saving': 'Saving…',
  'account.saved': 'Saved',
  'account.userName.hint': 'Shown on your comments. Latin letters and digits.',
  'account.avatar.upload': 'Upload a picture',
  'account.avatar.remove': 'Remove picture',
  'account.google.linked': 'Signs in with Google',
  'account.google.notLinked': 'Signs in with a password',
  'account.unconfirmed': ({ email }: Params) => `${email} is not confirmed yet.`,
  'account.unconfirmed.short': 'E-mail not confirmed',
  'account.resend': 'Send the link again',
  'account.resent': 'Link sent.',

  'confirm.done.title': 'E-mail confirmed',
  'confirm.done.text': 'That is everything. Your address is confirmed.',
  'confirm.failed.title': 'This link no longer works',
  'confirm.failed.text':
    'It may have expired or been used already. Ask for a new one in your account settings.',
  'confirm.back': 'Back to the comments',

  'form.postingAs': ({ name }: Params) => `Posting as ${name}`,

  'time.now': 'just now',
  'time.minutes': ({ count }: Params) => `${count} min ago`,
  'time.hours': ({ count }: Params) => `${count} h ago`,
} as const;

/** Values interpolated into a message. */
export type Params = Record<string, string | number>;

export type MessageKey = keyof typeof en;

export type Messages = {
  readonly [K in MessageKey]: (typeof en)[K] extends string ? string : (params: Params) => string;
};

export const uk: Messages = {
  'app.skipToContent': 'До вмісту',
  'app.tagline': 'SPA-застосунок: Коментарі',
  'app.stack': '.NET 10 · Angular 22 · MS SQL · RabbitMQ · Elasticsearch · Redis · Azure',
  'app.language': 'Мова',
  'app.title': 'Коментарі — Threadline',

  'list.title': 'Коментарі',
  'list.subtitle': 'Заглавні коментарі, по 25 на сторінку. Найновіші згори.',
  'list.caption': 'Заглавні коментарі з сортуванням за імʼям, e-mail і датою',
  'list.live.on': 'Оновлення в реальному часі',
  'list.live.off': 'Немає зʼєднання',
  'list.live.label': 'Наживо',
  'list.new': 'Новий коментар',
  'list.collapse': 'Згорнути',
  'list.pending': ({ count }) => `Нових коментарів: ${count} — показати`,
  'list.loading': 'Завантаження…',
  'list.empty': 'Коментарів ще немає. Будьте першим.',
  'list.error': 'Не вдалося завантажити коментарі.',
  'list.total': ({ count }) => `Заглавних коментарів: ${count}`,
  'list.page': ({ page, pages }) => `сторінка ${page} з ${pages}`,
  'list.pageCap': ({ pages }) =>
    `гортати можна перші ${pages} сторінок — далі звужуйте список сортуванням`,

  'column.userName': 'Імʼя',
  'column.email': 'E-mail',
  'column.date': 'Дата додавання',
  'column.comment': 'Коментар',
  'column.replies': 'Відповідей',
  'column.sortBy': ({ column }) => `Сортувати за: ${column}`,

  'paging.label': 'Сторінки коментарів',
  'paging.previous': '← Назад',
  'paging.next': 'Вперед →',
  'paging.goTo': ({ page }) => `Сторінка ${page}`,

  'thread.loading': 'Завантаження гілки…',
  'thread.error': 'Не вдалося завантажити гілку.',
  'thread.more': ({ count }) => `Показати ще (${count})`,
  'thread.reply': 'Відповісти',
  'thread.cancel': 'Скасувати',

  'attachment.open': ({ name }) => `Відкрити зображення ${name}`,
  'attachment.openText': ({ name }) => `Відкрити текстовий файл ${name}`,

  'lightbox.download': 'Завантажити',
  'lightbox.close': 'Закрити',
  'lightbox.loading': 'Завантаження…',
  'lightbox.loadFailed': 'Не вдалося завантажити файл.',

  'form.userName': 'Імʼя',
  'form.email': 'E-mail',
  'form.homePage': 'Домашня сторінка',
  'form.homePage.hint': 'Необовʼязкове поле.',
  'form.text': 'Текст',
  'form.file': 'Файл',
  'form.captcha': 'CAPTCHA',
  'form.captcha.placeholder': 'Символи з картинки',
  'form.captcha.refresh': 'Нова картинка',
  'form.captcha.alt': 'CAPTCHA: символи, які треба ввести',
  'form.preview': 'Перегляд',
  'form.preview.title': 'ПЕРЕГЛЯД',
  'form.preview.close': 'Закрити перегляд',
  'form.submit': 'Надіслати',
  'form.submitting': 'Надсилання…',
  'form.cancel': 'Скасувати',
  'form.tags.hint': ({ tags }) => `Дозволені теги: ${tags}. Кожен тег має бути закритий.`,
  'form.file.hint': ({ maxWidth, maxHeight, maxTextKb }) =>
    `JPG, GIF, PNG (зменшується до ${maxWidth}×${maxHeight}) або TXT до ${maxTextKb} КБ.`,
  'form.file.clear': 'Прибрати',
  'form.file.preview': 'Перегляд обраного зображення',
  'form.tags.group': 'Дозволені HTML-теги',
  'form.counter': ({ length, max }) => `${length} / ${max}`,

  'form.tag.italic': 'Курсив',
  'form.tag.bold': 'Напівжирний',
  'form.tag.code': 'Код',
  'form.tag.link': 'Посилання',

  'validation.required': 'Обовʼязкове поле.',
  'validation.tooShort': 'Закоротке значення.',
  'validation.tooLong': 'Задовге значення.',
  'validation.url': 'Вкажіть абсолютну http/https адресу.',
  'validation.email': 'Некоректний e-mail.',
  'validation.latin': 'Лише латинські літери й цифри.',
  'validation.unbalancedTag': ({ tag }) => `Тег <${tag}> не закритий або закритий неправильно.`,
  'validation.invalid': 'Некоректне значення.',

  'error.captcha': 'Не вдалося завантажити CAPTCHA. Спробуйте оновити її.',
  'error.submit': 'Не вдалося надіслати коментар.',
  'error.tooManyRequests': 'Забагато запитів. Зачекайте трохи і спробуйте ще раз.',

  'file.image.tooLarge': ({ maxMb }) => `Зображення не повинно перевищувати ${maxMb} МБ.`,
  'file.text.tooLarge': ({ maxKb }) => `Текстовий файл не повинен перевищувати ${maxKb} КБ.`,
  'file.wrongType': 'Дозволені лише JPG, GIF, PNG і TXT.',
  'file.size': ({ width, height }) => `${width}×${height}`,
  'file.willResize': ({ width, height, maxWidth, maxHeight }) =>
    `${width}×${height} → буде зменшено до ${maxWidth}×${maxHeight}`,

  'auth.signIn.title': 'Вхід',
  'auth.signIn.subtitle': 'Щоб писати від себе — зі своїм імʼям і фото.',
  'auth.signIn.action': 'Увійти',
  'auth.register.title': 'Створити акаунт',
  'auth.register.subtitle': 'Адреса і пароль. Нікнейм візьмемо з адреси, змінити можна будь-коли.',
  'auth.register.action': 'Створити акаунт',
  'auth.google': 'Увійти через Google',
  'auth.or': 'або',
  'auth.password': 'Пароль',
  'auth.password.hint': ({ min }) => `Щонайменше ${min} символів. Довжина — єдина вимога.`,
  'auth.working': 'Хвилинку…',
  'auth.signOut': 'Вийти',
  'auth.noAccount': 'Ще немає акаунта?',
  'auth.haveAccount': 'Вже маєте акаунт?',
  'auth.error.credentials': 'Невірна адреса або пароль.',
  'auth.error.generic': 'Щось пішло не так. Спробуйте ще раз.',

  'account.title': 'Акаунт',
  'account.settings': 'Налаштування акаунта',
  'account.save': 'Зберегти',
  'account.saving': 'Збереження…',
  'account.saved': 'Збережено',
  'account.userName.hint': 'Показується на ваших коментарях. Латинські літери й цифри.',
  'account.avatar.upload': 'Завантажити фото',
  'account.avatar.remove': 'Прибрати фото',
  'account.google.linked': 'Вхід через Google',
  'account.google.notLinked': 'Вхід за паролем',
  'account.unconfirmed': ({ email }) => `Адресу ${email} ще не підтверджено.`,
  'account.unconfirmed.short': 'Пошту не підтверджено',
  'account.resend': 'Надіслати лист ще раз',
  'account.resent': 'Лист надіслано.',

  'confirm.done.title': 'Пошту підтверджено',
  'confirm.done.text': 'Готово — адресу підтверджено.',
  'confirm.failed.title': 'Посилання більше не діє',
  'confirm.failed.text':
    'Воно могло застаріти або вже було використане. Замовте нове в налаштуваннях акаунта.',
  'confirm.back': 'До коментарів',

  'form.postingAs': ({ name }) => `Пишете як ${name}`,

  'time.now': 'щойно',
  'time.minutes': ({ count }) => `${count} хв тому`,
  'time.hours': ({ count }) => `${count} год тому`,
};
