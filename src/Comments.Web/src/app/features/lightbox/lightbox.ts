import { Component, ElementRef, effect, inject, viewChild } from '@angular/core';

import { LightboxService } from './lightbox.service';

/**
 * The file viewer, with the visual effects the assignment asks for.
 *
 * Written by hand rather than pulled from a library: Lightbox2 is jQuery-based and the assignment
 * links it as an <em>example</em> of the effect, not as a dependency. A component plus a CSS
 * transition gives the same result without adding jQuery to an Angular application.
 *
 * Built on the native `<dialog>` element, which brings focus trapping, Escape-to-close, inertness
 * of the page behind it and correct screen-reader semantics for free — all things a hand-rolled
 * overlay usually gets wrong.
 */
@Component({
  selector: 'app-lightbox',
  templateUrl: './lightbox.html',
  styleUrl: './lightbox.scss',
  host: {
    // A click on the backdrop lands on the <dialog> itself, not on the panel inside it. The keyboard
    // equivalent is Escape, which the native dialog already handles — so this lives on the host
    // rather than as a template click handler that would look keyboard-inaccessible.
    '(click)': 'closeOnBackdrop($event)',
  },
})
export class Lightbox {
  private readonly dialogRef = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly lightbox = inject(LightboxService);

  constructor() {
    effect(() => {
      const dialog = this.dialogRef().nativeElement;
      const hasContent = this.lightbox.content() !== null;

      if (hasContent && !dialog.open) {
        dialog.showModal();
      } else if (!hasContent && dialog.open) {
        dialog.close();
      }
    });
  }

  protected close(): void {
    this.lightbox.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialogRef().nativeElement) {
      this.close();
    }
  }
}
