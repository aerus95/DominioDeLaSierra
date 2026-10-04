import { Component, computed, effect, ElementRef, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CatalogService } from '../../core/catalog.service';
import { CartService } from '../../core/cart.service';
import { ProductCardComponent } from '../../shared/product-card.component';

@Component({ selector: 'ds-shop', standalone: true, imports: [FormsModule, ProductCardComponent], template: `
  <section class="page-hero shop-hero"><div class="shop-hero-image" aria-hidden="true"></div><div class="shell shop-hero-content"><p class="eyebrow">La colección</p><h1>Nuestros <em>vinos.</em></h1><p>Botellas nacidas en la Sierra de Francia, listas para encontrar su mesa.</p><div class="shop-hero-notes"><span>Rufete autóctona</span><span>Producción limitada</span><span>Envío desde bodega</span></div></div></section>
  <section class="shell shop-layout section-pad">
    <aside class="shop-sidebar">
      <p class="footer-label">Filtrar por</p>
      <button [class.selected]="selectedCategory() === 'all'" (click)="chooseCategory('all')">Todos los vinos</button>
      @for (category of catalog.categories(); track category.slug) {
        <button [class.selected]="selectedCategory() === category.slug" (click)="chooseCategory(category.slug)">{{ category.name }}</button>
      }
      <div class="shop-aside-note"><span>Rufete</span><p>Una variedad local, de ciclo largo y piel fina. La identidad de una sierra entera.</p></div>
    </aside>
    <div class="shop-content">
      <div #listingStart class="shop-toolbar">
        <span>{{ catalog.loading() ? 'Cargando…' : referenceLabel() }}</span>
        <label class="page-size">Mostrar
          <select [ngModel]="catalog.pageSize()" (ngModelChange)="changePageSize($event)" aria-label="Productos por página">
            @for (size of pageSizes; track size) { <option [ngValue]="size">{{ size }}</option> }
          </select>
        </label>
      </div>
      @if (catalog.error(); as message) { <p class="empty-state">{{ message }}</p> }
      <div class="product-grid">
        @for (product of catalog.items(); track product.id) {
          <ds-product-card [product]="product" [neighbors]="catalog.items()" (addToCart)="cart.add($event)" />
        } @empty {
          @if (!catalog.loading() && !catalog.error()) { <p class="empty-state">No hay vinos en esta categoría todavía.</p> }
        }
      </div>
      @if (catalog.totalPages() > 0) {
        <nav class="shop-pager" aria-label="Paginación del catálogo">
          <button type="button" (click)="goTo(catalog.page() - 1)" [disabled]="catalog.page() <= 1 || catalog.loading()">Anterior</button>
          @for (entry of pageList(); track $index) {
            @if (entry === 'gap') { <span class="pager-gap" aria-hidden="true">…</span> }
            @else {
              <button type="button" [class.selected]="entry === catalog.page()" [attr.aria-current]="entry === catalog.page() ? 'page' : null" (click)="goTo(entry)" [disabled]="catalog.loading()">{{ entry }}</button>
            }
          }
          <button type="button" (click)="goTo(catalog.page() + 1)" [disabled]="catalog.page() >= catalog.totalPages() || catalog.loading()">Siguiente</button>
        </nav>
      }
    </div>
  </section>
`, styles: [] })
export class ShopComponent {
  readonly pageSizes = CatalogService.pageSizes;
  readonly selectedCategory = signal('all');
  readonly page = signal(1);
  readonly pageSize = signal<number>(CatalogService.defaultPageSize);
  readonly referenceLabel = computed(() => this.catalog.totalItems() === 1 ? '1 referencia' : `${this.catalog.totalItems()} referencias`);
  readonly pageList = computed(() => pageWindow(this.catalog.page(), this.catalog.totalPages()));
  private readonly listingStart = viewChild<ElementRef<HTMLElement>>('listingStart');
  private scrollToListingAfterLoad = false;

  constructor(public readonly catalog: CatalogService, public readonly cart: CartService) {
    effect(() => {
      const category = this.selectedCategory();
      this.catalog.loadPage(this.page(), this.pageSize(), category === 'all' ? null : category);
    });
    effect(() => {
      if (this.catalog.loading() || !this.scrollToListingAfterLoad) return;
      this.scrollToListingAfterLoad = false;
      requestAnimationFrame(() => this.scrollToListing());
    });
  }

  chooseCategory(slug: string): void {
    this.scrollToListingAfterLoad = false;
    this.selectedCategory.set(slug);
    this.page.set(1);
  }

  changePageSize(size: number): void {
    const next = Number(size);
    if (!Number.isFinite(next) || next === this.pageSize()) return;
    this.scrollToListingAfterLoad = false;
    this.pageSize.set(next);
    this.page.set(1);
  }

  goTo(page: number): void {
    const last = this.catalog.totalPages();
    if (page < 1 || page > last || page === this.page()) return;
    this.scrollToListingAfterLoad = true;
    this.page.set(page);
  }

  private scrollToListing(): void {
    const listing = this.listingStart()?.nativeElement;
    if (!listing) return;
    const header = document.querySelector('.site-header');
    const covered = header instanceof HTMLElement ? Math.max(0, header.getBoundingClientRect().bottom) : 0;
    const top = listing.getBoundingClientRect().top + window.scrollY - covered - 8;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    window.scrollTo({ top: Math.max(0, top), behavior: reduced ? 'auto' : 'smooth' });
  }
}

function pageWindow(current: number, total: number): Array<number | 'gap'> {
  if (total < 1) return [];
  const wanted = [1, total, current - 1, current, current + 1].filter((page) => page >= 1 && page <= total);
  const pages = [...new Set(wanted)].sort((left, right) => left - right);
  const entries: Array<number | 'gap'> = [];
  for (const page of pages) {
    const previous = entries.at(-1);
    if (typeof previous === 'number' && page - previous > 1) entries.push('gap');
    entries.push(page);
  }
  return entries;
}
