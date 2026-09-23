import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Someone's picture, or their initial in a coloured circle.
 *
 * The colour is derived from the name rather than stored, so the same person is the same colour on
 * every page and a new account needs nothing generated for it.
 */
@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (url()) {
      <img class="avatar__image" [src]="url()" [alt]="name()" loading="lazy" decoding="async" />
    } @else {
      <span class="avatar__initial" [style.background]="colour()" aria-hidden="true">
        {{ initial() }}
      </span>
    }
  `,
  styles: `
    :host {
      display: inline-grid;
      place-items: center;
      width: var(--avatar-size, 2rem);
      height: var(--avatar-size, 2rem);
      border-radius: 50%;
      overflow: hidden;
      flex: none;
    }

    .avatar__image {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .avatar__initial {
      display: grid;
      place-items: center;
      width: 100%;
      height: 100%;
      color: #fff;
      font-size: calc(var(--avatar-size, 2rem) * 0.45);
      font-weight: 700;
      text-transform: uppercase;
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();
  readonly url = input<string | null | undefined>(null);

  protected readonly initial = computed(() => this.name().charAt(0).toUpperCase());

  protected readonly colour = computed(() => {
    const name = this.name();
    let hash = 0;

    for (let i = 0; i < name.length; i++) {
      hash = (hash * 31 + name.charCodeAt(i)) % 360;
    }

    // Fixed saturation and lightness: the hue varies, the contrast with white text does not.
    return `hsl(${hash} 55% 45%)`;
  });
}
