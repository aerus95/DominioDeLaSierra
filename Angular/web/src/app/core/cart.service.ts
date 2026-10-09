import { Injectable, computed, signal } from '@angular/core';
import { CartItem, Product } from './models';

const cartStorageKey = 'ds-cart';

@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly itemsSignal = signal<CartItem[]>(readCart());
  readonly items = this.itemsSignal.asReadonly();
  readonly itemCount = computed(() => this.itemsSignal().reduce((total, item) => total + item.quantity, 0));
  readonly subtotal = computed(() => this.itemsSignal().reduce((total, item) => total + item.product.price * item.quantity, 0));

  add(product: Product): void {
    this.itemsSignal.update((items) => {
      const existing = items.find((item) => item.product.id === product.id);
      return existing
        ? items.map((item) => item.product.id === product.id ? { ...item, quantity: item.quantity + 1 } : item)
        : [...items, { product, quantity: 1 }];
    });
    this.persist();
  }

  update(productId: string, quantity: number): void {
    if (quantity <= 0) {
      this.remove(productId);
      return;
    }
    this.itemsSignal.update((items) => items.map((item) => item.product.id === productId ? { ...item, quantity } : item));
    this.persist();
  }

  remove(productId: string): void {
    this.itemsSignal.update((items) => items.filter((item) => item.product.id !== productId));
    this.persist();
  }

  private persist(): void {
    try {
      sessionStorage.setItem(cartStorageKey, JSON.stringify(this.itemsSignal()));
    } catch {
      return;
    }
  }
}

function readCart(): CartItem[] {
  try {
    const raw = sessionStorage.getItem(cartStorageKey);
    const parsed = raw ? JSON.parse(raw) as CartItem[] : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}
