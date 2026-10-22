import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, catchError, expand, map, of, switchMap, timer } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CHECKOUT_CANCEL_MESSAGE,
  CHECKOUT_SUCCESS_MESSAGE,
  CHECKOUT_UNAVAILABLE_MESSAGE,
  CHECKOUT_UNKNOWN_MESSAGE,
  CheckoutOutcome,
  checkoutIsOpen,
  CheckoutOutcomeView,
  checkoutAmount,
  checkoutOutcomeCopy,
  checkoutOutcomeView,
  nextCheckoutPoll,
  paypalReturnShouldCapture,
  readCurrentCheckout
} from '../../core/checkout';
import { CheckoutService } from '../../core/checkout.service';

@Component({
  selector: 'ds-checkout-result',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="shell section-pad checkout-result">
      <p class="eyebrow">Caja</p>
      <h1>{{ heading() }}</h1>
      <p role="status">{{ detail() }}</p>
      @if (outcome(); as order) {
        <dl class="checkout-facts">
          <div><dt>Pedido</dt><dd>{{ order.number }}</dd></div>
          @if (amount(); as total) {
            <div><dt>Importe</dt><dd>{{ total }}</dd></div>
          }
        </dl>
      }
      <div class="checkout-actions">
        <a routerLink="/vinos" class="button button-dark">Seguir en la tienda</a>
        <a routerLink="/carrito" class="button">Volver a la caja</a>
      </div>
    </section>
  `,
  styles: [`
    .checkout-result { max-width: 40rem; }
    .checkout-result h1 { font-size: clamp(2.1rem, 8vw, 3.4rem); line-height: 1.1; margin: 0; }
    .checkout-result p { margin: 1rem 0 1.5rem; max-width: 36rem; }
    .checkout-facts { display: grid; gap: .85rem; margin: 0 0 2rem; }
    .checkout-facts div { display: flex; justify-content: space-between; gap: 1rem; border-top: 1px solid var(--line); padding-top: .8rem; }
    .checkout-facts dt { color: var(--muted); }
    .checkout-facts dd { margin: 0; text-align: right; }
    .checkout-actions { display: flex; flex-wrap: wrap; gap: 1rem; }
    @media (max-width: 560px) {
      .checkout-facts div { display: block; }
      .checkout-facts dd { text-align: left; margin-top: .25rem; }
      .checkout-actions { display: grid; }
    }
  `]
})
export class CheckoutResultComponent {
  private readonly checkout = inject(CheckoutService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  readonly view = signal<CheckoutOutcomeView>('loading');
  readonly outcome = signal<CheckoutOutcome | null>(null);
  readonly heading = signal(checkoutOutcomeCopy('loading'));
  readonly detail = signal(CHECKOUT_SUCCESS_MESSAGE);
  readonly amount = signal<string | null>(null);

  constructor() {
    if (environment.checkoutGate === 'closed') {
      this.showUnavailable();
      return;
    }

    this.checkout.availability().pipe(takeUntilDestroyed(this.destroyRef)).subscribe((allowed) => {
      if (!checkoutIsOpen(environment.checkoutGate, allowed)) {
        this.showUnavailable();
        return;
      }
      this.followPayment();
    });
  }

  private showUnavailable(): void {
    this.view.set('unknown');
    this.heading.set(CHECKOUT_UNAVAILABLE_MESSAGE);
    this.detail.set(CHECKOUT_UNAVAILABLE_MESSAGE);
  }

  private followPayment(): void {
    const current = readCurrentCheckout(this.storage());
    if (!current) {
      this.show('unknown', null, false);
      return;
    }

    let completed = 0;
    const request = () => this.checkout.outcome(current.orderId, current.token).pipe(
      map((outcome) => ({ outcome, failed: false as const })),
      catchError(() => of({ outcome: null, failed: true as const }))
    );
    const prepare = paypalReturnShouldCapture(this.route.snapshot.routeConfig?.path)
      ? this.checkout.capturePayPal(current.orderId, current.token).pipe(catchError(() => of(null)))
      : of(null);

    prepare.pipe(
      switchMap(() => request()),
      expand((result) => {
        completed += 1;
        this.show(checkoutOutcomeView(result.outcome?.status ?? null, result.failed), result.outcome, result.failed);
        const wait = nextCheckoutPoll(this.view(), completed);
        if (wait === null) return EMPTY;
        return timer(wait).pipe(switchMap(() => request()));
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  private show(view: CheckoutOutcomeView, outcome: CheckoutOutcome | null, failed: boolean): void {
    this.view.set(view);
    this.outcome.set(outcome);
    this.heading.set(checkoutOutcomeCopy(view));
    this.amount.set(outcome ? checkoutAmount(outcome.totalCents, outcome.currency) : null);
    if (view === 'verifying') {
      this.detail.set(this.route.snapshot.routeConfig?.path === 'checkout/cancelado'
        ? CHECKOUT_CANCEL_MESSAGE
        : CHECKOUT_SUCCESS_MESSAGE);
      return;
    }
    if (view === 'unknown') {
      this.heading.set('No podemos consultar el pago.');
      this.detail.set(failed
        ? 'No hemos podido consultar el pedido. Esto no indica que el pago haya fallado.'
        : CHECKOUT_UNKNOWN_MESSAGE);
      return;
    }
    this.detail.set('El estado corresponde al pedido guardado, no a la página de retorno.');
  }

  private storage(): Storage | null {
    try {
      return sessionStorage;
    } catch {
      return null;
    }
  }
}
