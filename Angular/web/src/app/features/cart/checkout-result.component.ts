import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CHECKOUT_CANCEL_MESSAGE, CHECKOUT_SUCCESS_MESSAGE } from '../../core/checkout';

@Component({
  selector: 'ds-checkout-result',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="shell section-pad checkout-result">
      <p class="eyebrow">{{ cancelled ? 'Pago cancelado' : 'Pago en verificación' }}</p>
      <h1>{{ cancelled ? 'No se ha completado el pago.' : 'Estamos verificando el pago.' }}</h1>
      <p role="status">{{ message }}</p>
      <a routerLink="/carrito" class="button button-dark">Volver a la caja</a>
    </section>
  `,
  styles: [`.checkout-result{max-width:40rem;padding-block:4rem}h1{font-size:2.4rem;line-height:1.1}p{margin:1rem 0 2rem}`]
})
export class CheckoutResultComponent {
  private readonly route = inject(ActivatedRoute);
  readonly cancelled = this.route.snapshot.data['cancelled'] === true;
  readonly message = this.cancelled ? CHECKOUT_CANCEL_MESSAGE : CHECKOUT_SUCCESS_MESSAGE;
}
