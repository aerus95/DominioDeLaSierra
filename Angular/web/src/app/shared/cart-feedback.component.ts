import { Component, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CartFeedbackService } from '../core/cart-feedback.service';
import { CartService } from '../core/cart.service';
import { bottleImage } from '../core/product-visual';

@Component({ selector:'ds-cart-feedback', standalone:true, imports:[RouterLink, DecimalPipe], template:`
  <div class="cart-feedback-region" aria-live="polite" aria-atomic="true">
    @if (feedback.product(); as product) {
      <aside class="cart-added-note" (mouseenter)="feedback.pause()" (mouseleave)="feedback.resume()" (focusin)="feedback.pause()" (focusout)="feedback.resume()">
        <div class="cart-added-photo"><img [src]="bottleImage(product)" alt="" width="320" height="960"></div>
        <div class="cart-added-copy"><p><span aria-hidden="true">✓</span> Añadido a tu cesta</p><strong>{{ product.name }}</strong><small>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'producto' : 'productos' }} · {{ cart.subtotal() | number:'1.2-2' }} €</small><a routerLink="/carrito" (click)="feedback.dismiss()">Ver mi cesta <span aria-hidden="true">↗</span></a></div>
        <button class="cart-added-close" type="button" (click)="feedback.dismiss()" aria-label="Cerrar confirmación">×</button>
      </aside>
    }
  </div>
` })
export class CartFeedbackComponent {
  readonly feedback = inject(CartFeedbackService);
  readonly cart = inject(CartService);
  readonly bottleImage = bottleImage;
}
