import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { Auth } from './core/auth/auth';
import { I18n } from './core/i18n/i18n';
import { Lightbox } from './features/lightbox/lightbox';
import { AccountMenu } from './shared/account-menu';
import { LanguageSwitch } from './shared/language-switch';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
  imports: [RouterLink, RouterOutlet, Lightbox, LanguageSwitch, AccountMenu],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly i18n = inject(I18n);
  private readonly auth = inject(Auth);

  protected readonly t = this.i18n.t;

  constructor() {
    // The document's language and title belong to whatever the reader chose, including on the
    // first render — index.html only carries the default.
    this.i18n.use(this.i18n.locale());

    // The session cookie may already be there from a previous visit; asking once at start-up is
    // what lets the header render the right thing instead of guessing.
    void this.auth.restore();
  }
}
