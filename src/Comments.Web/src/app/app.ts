import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { I18n } from './core/i18n/i18n';
import { Lightbox } from './features/lightbox/lightbox';
import { LanguageSwitch } from './shared/language-switch';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
  imports: [RouterOutlet, Lightbox, LanguageSwitch],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly i18n = inject(I18n);

  protected readonly t = this.i18n.t;

  constructor() {
    // The document's language and title belong to whatever the reader chose, including on the
    // first render — index.html only carries the default.
    this.i18n.use(this.i18n.locale());
  }
}
