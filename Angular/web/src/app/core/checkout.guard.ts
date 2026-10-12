import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { environment } from '../../environments/environment';
import { CheckoutService } from './checkout.service';

export const checkoutAccessGuard: CanActivateFn = () => {
  const router = inject(Router);
  if (environment.checkoutGate === 'closed') return router.createUrlTree(['/carrito']);
  return inject(CheckoutService).availability().pipe(
    map((allowed) => allowed || router.createUrlTree(['/carrito']))
  );
};
