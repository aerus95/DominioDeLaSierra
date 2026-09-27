import { bootstrapApplication } from '@angular/platform-browser';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import es from '@angular/common/locales/es';
import { AppComponent } from './app/app.component';
import { appRoutes } from './app/app.routes';
import { finishWelcomeLoading, failWelcomeLoading } from './app/core/loading';

registerLocaleData(es);
bootstrapApplication(AppComponent, {
  providers: [
    { provide: LOCALE_ID, useValue: 'es' },
    provideHttpClient(),
    provideRouter(appRoutes, withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' })),
    provideAnimationsAsync()
  ]
}).then(async (app) => {
  await app.whenStable();
  requestAnimationFrame(() => finishWelcomeLoading());
}).catch((error) => {
  console.error(error);
  failWelcomeLoading();
});
