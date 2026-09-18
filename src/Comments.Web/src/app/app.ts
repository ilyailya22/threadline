import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { Lightbox } from './features/lightbox/lightbox';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
  imports: [RouterOutlet, Lightbox],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
