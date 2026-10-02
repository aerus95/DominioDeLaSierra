import { Injectable, signal } from '@angular/core';
import { Product } from './models';
import { animateBottleToCart } from './cart-motion';

@Injectable({ providedIn:'root' })
export class CartFeedbackService {
  readonly product = signal<Product | null>(null);
  readonly pendingProduct = signal<Product | null>(null);
  private timer?: ReturnType<typeof setTimeout>;
  private sequence = 0;
  async present(event: MouseEvent, product: Product): Promise<void> {
    const ticket = ++this.sequence;
    clearTimeout(this.timer);
    this.product.set(null);
    this.pendingProduct.set(product);
    // A visual failure must never turn a successful addition into a failed action.
    await animateBottleToCart(event, product).catch(() => undefined);
    if (ticket !== this.sequence) return;
    this.pendingProduct.set(null);
    this.product.set(product);
    this.resume();
  }
  pause(): void { clearTimeout(this.timer); }
  resume(): void { clearTimeout(this.timer); if (this.product()) this.timer = setTimeout(() => this.dismiss(), 4800); }
  dismiss(): void { clearTimeout(this.timer); this.product.set(null); }
}
