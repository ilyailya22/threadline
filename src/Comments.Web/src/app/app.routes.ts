import type { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    // Lazily loaded even though it is the main page: it costs nothing and keeps the shell, which
    // every route pays for, down to what the header needs.
    loadComponent: () =>
      import('./features/comments/comments-page/comments-page').then((m) => m.CommentsPage),

    // The document title follows the chosen language, so the shell sets it rather than the route.
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/account/sign-in-page').then((m) => m.SignInPage),
    data: { mode: 'signIn' },
  },
  {
    // The same page, one word different: see SignInPage.
    path: 'register',
    loadComponent: () => import('./features/account/sign-in-page').then((m) => m.SignInPage),
    data: { mode: 'register' },
  },
  {
    path: 'account',
    loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
  },
  {
    path: 'confirm-email',
    loadComponent: () =>
      import('./features/account/confirm-email-page').then((m) => m.ConfirmEmailPage),
  },
  { path: '**', redirectTo: '' },
];
