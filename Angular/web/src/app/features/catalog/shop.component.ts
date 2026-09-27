import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CatalogService } from '../../core/catalog.service';
import { CartService } from '../../core/cart.service';
import { ProductCardComponent } from '../../shared/product-card.component';

@Component({ selector: 'ds-shop', standalone: true, imports: [FormsModule, ProductCardComponent], template: `
  <section class="page-hero shop-hero"><div class="shop-hero-image" aria-hidden="true"></div><div class="shell shop-hero-content"><p class="eyebrow">La colección</p><h1>Nuestros <em>vinos.</em></h1><p>Botellas nacidas en la Sierra de Francia, listas para encontrar su mesa.</p><div class="shop-hero-notes"><span>Rufete autóctona</span><span>Producción limitada</span><span>Envío desde bodega</span></div></div></section>
  <section class="shell shop-layout section-pad"><aside class="shop-sidebar"><p class="footer-label">Filtrar por</p><button [class.selected]="selectedCategory() === 'all'" (click)="selectedCategory.set('all')">Todos los vinos <span>{{ catalog.products().length }}</span></button>@for (category of catalog.categories(); track category.slug) { <button [class.selected]="selectedCategory() === category.slug" (click)="selectedCategory.set(category.slug)">{{ category.name }} <span>{{ countCategory(category.slug) }}</span></button> }<div class="shop-aside-note"><span>Rufete</span><p>Una variedad local, de ciclo largo y piel fina. La identidad de una sierra entera.</p></div></aside><div class="shop-content"><div class="shop-toolbar"><span>{{ catalog.loading() ? 'Cargando…' : filteredProducts().length + ' referencias' }}</span><select [ngModel]="sortMode()" (ngModelChange)="sortMode.set($event)" aria-label="Ordenar productos"><option value="featured">Destacados</option><option value="price-low">Precio menor</option><option value="price-high">Precio mayor</option><option value="name">Nombre</option></select></div>@if (catalog.error(); as message) { <p class="empty-state">{{ message }}</p> }<div class="product-grid">@for (product of filteredProducts(); track product.id) { <ds-product-card [product]="product" (addToCart)="cart.add($event)" /> } @empty { @if (!catalog.loading() && !catalog.error()) { <p class="empty-state">No hay vinos en esta categoría todavía.</p> } }</div></div></section>
`, styles: [] })
export class ShopComponent {
  readonly selectedCategory = signal('all');
  readonly sortMode = signal('featured');
  readonly filteredProducts = computed(() => {
    const category = this.selectedCategory();
    const list = category === 'all' ? [...this.catalog.products()] : this.catalog.products().filter((product) => product.categorySlug === category);
    if (this.sortMode() === 'price-low') return list.sort((a, b) => a.price - b.price);
    if (this.sortMode() === 'price-high') return list.sort((a, b) => b.price - a.price);
    if (this.sortMode() === 'name') return list.sort((a, b) => a.name.localeCompare(b.name));
    return list;
  });
  constructor(public readonly catalog: CatalogService, public readonly cart: CartService) {}
  countCategory(slug: string): number { return this.catalog.products().filter((product) => product.categorySlug === slug).length; }
}
