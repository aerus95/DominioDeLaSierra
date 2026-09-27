import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { CatalogService } from '../../core/catalog.service';
import { CartService } from '../../core/cart.service';

@Component({ selector: 'ds-product-detail', standalone: true, imports: [RouterLink, DecimalPipe], template: `
  @if (catalog.loading()) {
    <section class="shell section-pad"><p class="empty-state">Cargando el vino…</p></section>
  } @else {
    @if (product(); as item) {
      <section class="product-detail shell section-pad"><div class="detail-image"><img [src]="item.image" [alt]="item.name"></div><div class="detail-copy"><p class="eyebrow">{{ item.categoryName }} · {{ item.reference }}</p><h1>{{ item.name }}</h1><p class="detail-price">{{ item.price | number:'1.2-2' }} €</p><p class="lead">{{ item.description }}</p><div class="detail-specs"><div><span>Variedad</span><strong>{{ item.grape }}</strong></div><div><span>Elaboración</span><strong>Parcela y calma</strong></div><div><span>Graduación</span><strong>{{ item.alcohol }}</strong></div></div><button class="button button-dark" (click)="cart.add(item); added = true">{{ added ? 'Añadido al carrito ✓' : 'Añadir al carrito' }} <span>↗</span></button><a routerLink="/vinos" class="back-link">← Volver a todos los vinos</a></div></section>
    } @else {
      <section class="shell section-pad"><p class="empty-state">No hemos encontrado ese vino.</p><a routerLink="/vinos" class="underline-link">Volver a la colección</a></section>
    }
  }
`, styles: [] })
export class ProductDetailComponent {
  added = false;
  private readonly route = inject(ActivatedRoute);
  readonly catalog = inject(CatalogService);
  public readonly cart = inject(CartService);
  private readonly slug = toSignal(this.route.paramMap.pipe(map((params) => params.get('slug') ?? '')), { initialValue: '' });
  readonly product = computed(() => this.catalog.getProduct(this.slug()));
}
