import type { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    // Lazily loaded even though there is one route today: it costs nothing now and means adding a
    // second feature later does not require restructuring the bundle.
    loadComponent: () =>
      import('./features/comments/comments-page/comments-page').then((m) => m.CommentsPage),

    // The document title follows the chosen language, so the shell sets it rather than the route.
  },
  { path: '**', redirectTo: '' },
];
