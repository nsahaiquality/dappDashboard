import { Component, computed, input } from '@angular/core';

/** Stroke icons (24×24 grid). Decorative: pair with visible text or an aria-label on the control. */
const PATHS: Record<string, string[]> = {
  logo: ['M3 17l5-5 4 4 8-8', 'M14 8h6v6'],
  overview: ['M3 3h7v9H3z', 'M14 3h7v5h-7z', 'M14 12h7v9h-7z', 'M3 16h7v5H3z'],
  assets: ['M21 8l-9-5-9 5 9 5 9-5z', 'M3 8v8l9 5 9-5V8'],
  remarketing: ['M4 4h16v4H4z', 'M6 8v12h12V8', 'M10 12h4'],
  vendors: ['M3 21h18', 'M5 21V8l7-5 7 5v13', 'M9 21v-6h6v6'],
  refinancing: ['M21 12a9 9 0 1 1-3-6.7', 'M21 4v5h-5'],
  sources: ['M4 5c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3z', 'M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5', 'M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3'],
  reports: ['M14 3v5h5', 'M5 3h9l5 5v13H5z', 'M9 13h6', 'M9 17h6'],
  pause: ['M7 4h3v16H7z', 'M14 4h3v16h-3z'],
  play: ['M7 4l13 8-13 8z'],
  download: ['M12 3v12', 'M7 10l5 5 5-5', 'M5 21h14'],
  search: ['M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z', 'M20 20l-3.5-3.5'],
  close: ['M6 6l12 12', 'M18 6L6 18'],
  up: ['M12 19V5', 'M6 11l6-6 6 6'],
  down: ['M12 5v14', 'M6 13l6 6 6-6'],
  chevron: ['M9 6l6 6-6 6'],
  filter: ['M3 5h18', 'M6 12h12', 'M10 19h4'],
};

@Component({
  selector: 'app-icon',
  host: { 'aria-hidden': 'true', class: 'icon' },
  template: `<svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    [attr.stroke-width]="stroke()" stroke-linecap="round" stroke-linejoin="round">
    @for (d of paths(); track $index) { <path [attr.d]="d" /> }
  </svg>`,
})
export class Icon {
  readonly name = input.required<string>();
  readonly size = input(18);
  readonly stroke = input(1.8);
  protected readonly paths = computed(() => PATHS[this.name()] ?? []);
}
