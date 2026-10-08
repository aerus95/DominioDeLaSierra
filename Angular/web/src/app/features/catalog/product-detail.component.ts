import { CartFeedbackService } from '../../core/cart-feedback.service';
import { Product } from '../../core/models';
import { Component, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, of, switchMap } from 'rxjs';
import { CatalogService } from '../../core/catalog.service';
import { CartService } from '../../core/cart.service';
import { showsWineSpec } from '../../core/wine-display';

@Component({ selector: 'ds-product-detail', standalone: true, imports: [RouterLink, DecimalPipe], template: `
  <nav class="shell product-back-nav" aria-label="Volver a la colección"><a routerLink="/vinos"><span aria-hidden="true">←</span> Volver a nuestros vinos</a></nav>
  @if (product() === undefined) {
    <section class="shell section-pad"><p class="empty-state">Cargando el vino…</p></section>
  } @else {
    @if (product(); as item) {
      <section class="product-detail shell section-pad"><div class="detail-image"><img [src]="item.image" [alt]="item.name"></div><div class="detail-copy"><p class="eyebrow">{{ item.categoryName }} · {{ item.reference }}</p><h1>{{ item.name }}</h1><p class="detail-price">{{ item.price | number:'1.2-2' }} €</p><p class="lead">{{ item.description }}</p><div class="detail-specs">@if (showsWineSpec(item.grape)) { <div><span>Variedad</span><strong>{{ item.grape }}</strong></div> }<div><span>Elaboración</span><strong>Parcela y calma</strong></div>@if (showsWineSpec(item.alcohol)) { <div><span>Graduación</span><strong>{{ item.alcohol }}</strong></div> }</div><button class="button button-dark" (click)="addProduct($event, item)">{{ added ? 'Añadido al carrito ✓' : 'Añadir al carrito' }} <span>↗</span></button><a routerLink="/vinos" class="back-link">← Volver a todos los vinos</a></div></section>
    } @else {
      <section class="shell section-pad"><p class="empty-state">No hemos encontrado ese vino.</p><a routerLink="/vinos" class="underline-link">Volver a la colección</a></section>
    }
  }
`, styles: [] })
export class ProductDetailComponent {
  readonly showsWineSpec = showsWineSpec;
  added = false;
  private readonly feedback = inject(CartFeedbackService);
  addProduct(event: MouseEvent, product: Product): void { this.cart.add(product); this.added = true; void this.feedback.present(event, product); }
  private readonly route = inject(ActivatedRoute);
  private readonly catalog = inject(CatalogService);
  public readonly cart = inject(CartService);
  private readonly slug = toSignal(this.route.paramMap.pipe(map((params) => params.get('slug') ?? '')), { initialValue: '' });
  readonly product = toSignal(
    toObservable(this.slug).pipe(
      switchMap((slug) => slug ? this.catalog.getProductBySlug(slug) : of(null))
    )
  );
}
