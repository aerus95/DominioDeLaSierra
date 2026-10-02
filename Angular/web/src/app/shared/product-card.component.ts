import { Component, HostListener, inject, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CartFeedbackService } from '../core/cart-feedback.service';
import { ModalFocusDirective } from './modal-focus.directive';
import { Product } from '../core/models';
import { CatalogService } from '../core/catalog.service';

@Component({
  selector: 'ds-product-card', standalone: true, imports: [RouterLink, DecimalPipe, ModalFocusDirective],
  template: `
    <article class="product-card">
      <a class="product-image" [routerLink]="['/vinos', product().slug]"><img [src]="product().image" [alt]="product().name" loading="lazy"><span class="product-category">{{ product().categoryName }}</span></a>
      <button type="button" class="quick-view-trigger" (click)="openQuickView()">Vista rápida <span>＋</span></button>
      <div class="product-info"><div><p class="product-ref">{{ product().reference }} · {{ product().categoryName }}</p><h3><a [routerLink]="['/vinos', product().slug]">{{ product().name }}</a></h3></div><strong>{{ product().price | number:'1.2-2' }} €</strong></div>
      <div class="product-actions"><button class="text-button add-cart-button" (click)="addProduct($event, product())">Añadir al carrito <span>＋</span></button></div>
    </article>
    @if (quickProduct(); as active) { <div class="quick-view-backdrop" (click)="closeQuickView()"><section class="quick-view-modal" dsModalFocus role="dialog" aria-modal="true" [attr.aria-label]="'Vista rápida de ' + active.name" (click)="$event.stopPropagation()"><button type="button" class="quick-view-close" (click)="closeQuickView()" aria-label="Cerrar vista rápida"><span>×</span></button><button type="button" class="quick-product-nav quick-product-prev" (click)="changeProduct(-1)" aria-label="Ver producto anterior">←</button><button type="button" class="quick-product-nav quick-product-next" (click)="changeProduct(1)" aria-label="Ver producto siguiente">→</button><div class="quick-view-gallery"><img [src]="galleryImages(active)[quickImage()]" [alt]="active.name"><div class="quick-view-thumbs">@for (image of galleryImages(active); track image; let index = $index) { <button type="button" [class.active]="quickImage() === index" (click)="quickImage.set(index)"><img [src]="image" alt=""></button> }</div></div><div class="quick-view-copy"><p class="eyebrow">{{ active.categoryName }} · {{ active.vintage }}</p><h2>{{ active.name }}</h2><strong class="quick-view-price">{{ active.price | number:'1.2-2' }} €</strong><p>{{ active.description }}</p><dl><div><dt>Variedad</dt><dd>{{ active.grape }}</dd></div><div><dt>Origen</dt><dd>San Esteban de la Sierra</dd></div><div><dt>Graduación</dt><dd>{{ active.alcohol }}</dd></div></dl><button class="button button-dark full-width" (click)="addProduct($event, active)">Añadir a mi caja <span>＋</span></button><a [routerLink]="['/vinos', active.slug]" (click)="closeQuickView()" class="underline-link">Ver ficha completa <span>↗</span></a></div></section></div> }
  `
})
export class ProductCardComponent {
  private readonly catalog = inject(CatalogService);
  private readonly feedback = inject(CartFeedbackService);
  readonly product = input.required<Product>();
  readonly addToCart = output<Product>();
  readonly quickProduct = signal<Product | null>(null);
  readonly quickImage = signal(0);

  galleryImages(product: Product): string[] {
    const contextual = (product.categorySlug || product.categoryId) === 'ancestral' ? '/assets/brand/rufete-ancestral-web.jpg' : '/assets/brand/rufete-vineyard-v2.png';
    return [...new Set([product.image, contextual, '/assets/brand/rufete-story-web.jpg'])];
  }

  openQuickView(): void { this.quickImage.set(0); this.quickProduct.set(this.product()); }
  closeQuickView(): void { this.quickProduct.set(null); }
  changeProduct(delta: number): void {
    const current = this.quickProduct();
    if (!current || !this.catalog.products().length) return;
    const products = this.catalog.products();
    const index = products.findIndex((product) => product.id === current.id);
    this.quickProduct.set(products[(index + delta + products.length) % products.length]);
    this.quickImage.set(0);
  }

  addProduct(event: MouseEvent, product: Product): void {
    this.addToCart.emit(product);
    void this.feedback.present(event, product);
  }

  @HostListener('window:resize') onResize(): void { if (window.innerWidth <= 600) this.closeQuickView(); }
  @HostListener('document:keydown.escape') onEscape(): void { this.closeQuickView(); }
}
