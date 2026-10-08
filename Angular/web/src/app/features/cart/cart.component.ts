import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CartService } from '../../core/cart.service';
import { CatalogService } from '../../core/catalog.service';
import { PackComponent, Product } from '../../core/models';
import { packQuantityLabel } from '../../core/pack-contents';
import { categoryGrapeLabel } from '../../core/wine-display';

interface PackContentsState {
  loading: boolean;
  error: string | null;
  items: PackComponent[];
}

@Component({ selector: 'ds-cart', standalone: true, imports: [RouterLink, DecimalPipe, FormsModule], template: `
  <section class="cart-hero"><div class="cart-hero-image"><img [src]="cartVisual()" alt=""></div><div class="cart-hero-overlay"></div><div class="shell"><p class="eyebrow light">Tu selección</p><h1>Botellas con<br><em>un lugar detrás.</em></h1><div class="cart-hero-meta"><span>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'botella' : 'botellas' }}</span><span>Envío peninsular</span><span>Compra segura</span></div><div class="cart-journey" aria-label="Proceso de compra"><button [class.active]="step() === 'selection'" [class.done]="step() !== 'selection'" (click)="step.set('selection')"><i>01</i><span>Tu selección</span><small>Vinos y cantidades</small></button><button [disabled]="!cart.items().length" [class.active]="step() === 'details'" [class.done]="step() === 'review'" (click)="step.set('details')"><i>02</i><span>Datos de envío</span><small>Destino del pedido</small></button><button [disabled]="step() !== 'review'" [class.active]="step() === 'review'"><i>03</i><span>Revisión y pago</span><small>TPV próximamente</small></button></div></div></section>
  <section class="shell cart-layout section-pad"><div class="cart-harvest" aria-hidden="true"></div>@if (cart.items().length) {
    @if (step() === 'selection') { <div class="cart-lines"><div class="cart-lines-head"><h2>Tu caja</h2><a routerLink="/vinos">Seguir descubriendo ↗</a></div>@for (item of cart.items(); track item.product.id) { <article class="cart-line"><div class="cart-line-image"><img [src]="item.product.image" [alt]="item.product.name"><span>{{ item.product.vintage }}</span></div><div><p class="product-ref">{{ categoryGrapeLabel(item.product.categoryName, item.product.grape) }}</p><h3>{{ item.product.name }}</h3><button class="remove-button" (click)="cart.remove(item.product.id)">Eliminar de la caja</button></div><div class="quantity"><button (click)="cart.update(item.product.id, item.quantity - 1)" aria-label="Restar una unidad">−</button><span>{{ item.quantity }}</span><button (click)="cart.update(item.product.id, item.quantity + 1)" aria-label="Sumar una unidad">+</button></div><strong>{{ item.product.price * item.quantity | number:'1.2-2' }} €</strong>@if (item.product.kind === 'Pack') { <div class="pack-panel"><button type="button" class="pack-toggle" (click)="togglePack(item.product)" [attr.aria-expanded]="isPackOpen(item.product.id)">{{ isPackOpen(item.product.id) ? 'Ver menos' : 'Ver más' }}</button>@if (isPackOpen(item.product.id)) { <div class="pack-contents">@if (packContents()[item.product.slug]; as contents) { @if (contents.loading) { <p class="pack-contents-note">Cargando el contenido del pack.</p> } @else if (contents.error) { <p class="pack-contents-note">{{ contents.error }}</p> } @else if (contents.items.length === 0) { <p class="pack-contents-note">Este pack no detalla su contenido.</p> } @else { @for (component of contents.items; track component.productId) { <article class="pack-component">@if (component.image) { <img class="pack-component-image" [src]="component.image" [alt]="component.name"> } @else { <span class="pack-component-placeholder" aria-hidden="true"></span> }<div><h4>{{ component.name }}</h4><p>{{ component.description }}</p><small>{{ packQuantityLabel(component.quantity, item.quantity) }}</small></div></article> } } } @else { <p class="pack-contents-note">Cargando el contenido del pack.</p> }</div> }</div> }</article> }<div class="cart-trust"><span>Embalaje protegido</span><span>Atención directa de bodega</span><span>Vino enviado desde origen</span></div></div><aside class="cart-summary"><p class="eyebrow">Resumen de la caja</p><div><span>Subtotal</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><div><span>Envío</span><span>Se calcula con tu dirección</span></div><div class="coupon-box"><label for="coupon-selection">¿Tienes un cupón?</label><div><input id="coupon-selection" [(ngModel)]="couponCode" placeholder="Código"><button type="button" (click)="applyCoupon()">Aplicar</button></div>@if (couponStatus()) { <small>{{ couponStatus() }}</small> }</div><hr><div class="summary-total"><span>Total estimado</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><button class="button button-dark full-width" (click)="step.set('details')">Continuar con mis datos <span>↗</span></button><small>No se realizará ningún cobro en esta versión.</small><blockquote>“De la Sierra de Francia a tu mesa.”</blockquote></aside> }
    @if (step() === 'details') { <div class="checkout-form-panel"><button class="checkout-back" (click)="step.set('selection')">← Volver a mi caja</button><p class="eyebrow">02 · Datos de envío</p><h2>El próximo destino<br><em>de estas botellas.</em></h2><p>Necesitamos lo justo para que el vino llegue bien y podamos avisarte durante el recorrido.</p><form #shippingForm="ngForm" (ngSubmit)="shippingForm.valid && step.set('review')"><fieldset class="shipping-block"><legend><i>01</i><span><strong>Persona de contacto</strong><small>Para confirmar y seguir el pedido</small></span></legend><div class="form-row"><label>Nombre y apellidos<input name="name" [(ngModel)]="customer.name" autocomplete="name" required placeholder="Nombre de quien recibe"></label><label>Correo electrónico<input name="email" [(ngModel)]="customer.email" type="email" autocomplete="email" required placeholder="nombre@correo.com"></label></div><label>Teléfono<input name="phone" [(ngModel)]="customer.phone" type="tel" autocomplete="tel" required placeholder="+34 600 000 000"></label></fieldset><fieldset class="shipping-block"><legend><i>02</i><span><strong>Dirección de entrega</strong><small>Envíos disponibles en Península</small></span></legend><label>Dirección<input name="address" [(ngModel)]="customer.address" autocomplete="street-address" required placeholder="Calle, número, piso y puerta"></label><div class="shipping-grid"><label>Código postal<input name="postal" [(ngModel)]="customer.postal" inputmode="numeric" autocomplete="postal-code" pattern="[0-9]{5}" maxlength="5" required placeholder="00000"></label><label>Localidad<input name="city" [(ngModel)]="customer.city" autocomplete="address-level2" required></label><label>Provincia<input name="province" [(ngModel)]="customer.province" autocomplete="address-level1" required></label></div><label>Indicaciones para la entrega <input name="notes" [(ngModel)]="customer.notes" placeholder="Opcional · horario, portal o referencia"></label></fieldset><label class="privacy-check"><input type="checkbox" name="privacy" [(ngModel)]="customer.privacy" required><span>Acepto la <a routerLink="/privacidad">política de privacidad</a> para preparar este pedido.</span></label><button class="button button-dark checkout-submit" [disabled]="shippingForm.invalid">Revisar mi pedido <span>↗</span></button></form></div><aside class="cart-summary checkout-mini-summary"><div class="summary-title"><p class="eyebrow">Tu caja</p><button type="button" (click)="step.set('selection')">Editar selección</button></div><div class="mini-cart-products">@for (item of cart.items(); track item.product.id) { <article><img [src]="item.product.image" alt=""><span><strong>{{ item.product.name }}</strong><small>{{ item.product.price | number:'1.2-2' }} €</small></span><div class="quantity compact"><button type="button" (click)="cart.update(item.product.id, item.quantity - 1)" aria-label="Restar una unidad">−</button><span>{{ item.quantity }}</span><button type="button" (click)="cart.update(item.product.id, item.quantity + 1)" aria-label="Sumar una unidad">+</button></div></article> }</div><div class="coupon-box"><label for="coupon-details">Cupón</label><div><input id="coupon-details" [(ngModel)]="couponCode" placeholder="Código promocional"><button type="button" (click)="applyCoupon()">Aplicar</button></div>@if (couponStatus()) { <small>{{ couponStatus() }}</small> }</div><hr><div class="summary-total"><span>Subtotal</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><small>Podrás comprobar todo antes del pago.</small></aside> }
    @if (step() === 'review') { <div class="checkout-review"><button class="checkout-back" (click)="step.set('details')">← Editar mis datos</button><p class="eyebrow">03 · Revisión</p><h2>Todo listo para<br><em>confirmar.</em></h2><div class="review-address"><span>Entrega</span><strong>{{ customer.name }}</strong><p>{{ customer.address }} · {{ customer.postal }} {{ customer.city }} · {{ customer.province }}</p><small>{{ customer.email }} · {{ customer.phone }}</small></div><div class="review-products">@for (item of cart.items(); track item.product.id) { <div><img [src]="item.product.image" alt=""><span>{{ item.quantity }} × {{ item.product.name }}</span><strong>{{ item.product.price * item.quantity | number:'1.2-2' }} €</strong></div> }</div></div><aside class="cart-summary payment-summary"><p class="eyebrow">Total del pedido</p><div><span>Productos</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><div><span>Envío</span><span>Pendiente de cálculo</span></div><hr><div class="summary-total"><span>Total provisional</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><button class="button button-dark full-width" disabled>Pago disponible próximamente</button><small>El pedido no se enviará ni cobrará hasta conectar el TPV y confirmar el coste de transporte.</small></aside> }
  } @else { <div class="empty-cart"><span class="empty-glyph">DS</span><p class="eyebrow">Tu caja espera</p><h2>Aún no has elegido<br><em>tu primer vino.</em></h2><p>Recorre nuestra colección y encuentra la botella que te lleve de vuelta a la Sierra.</p><a routerLink="/vinos" class="button button-dark">Descubrir los vinos <span>↗</span></a><div class="empty-cart-notes"><span>Rufete autóctona</span><span>8.000 botellas al año</span><span>Desde San Esteban</span></div></div> }</section>
`, styles: [] })
export class CartComponent {
  readonly packQuantityLabel = packQuantityLabel;
  readonly categoryGrapeLabel = categoryGrapeLabel;
  readonly step = signal<'selection' | 'details' | 'review'>('selection');
  readonly couponStatus = signal('');
  private readonly expandedPackIds = signal<ReadonlySet<string>>(new Set());
  readonly packContents = signal<Record<string, PackContentsState>>({});
  private readonly catalog = inject(CatalogService);
  private readonly destroyRef = inject(DestroyRef);
  couponCode = '';
  customer = { name:'', email:'', phone:'', address:'', postal:'', city:'', province:'', notes:'', privacy:false };
  readonly cartVisual = computed(() => {
    const category = this.cart.items()[0]?.product.categoryId;
    if (category === 'ancestral') return '/assets/brand/rufete-ancestral-web.jpg';
    if (category === 'blancos') return '/assets/brand/contact-cellar-web.jpg';
    if (category === 'packs') return '/assets/brand/visit-experience-web.jpg';
    return '/assets/brand/rufete-story-web.jpg';
  });
  constructor(public readonly cart: CartService, route: ActivatedRoute) {
    if (route.snapshot.queryParamMap.get('paso') === 'datos' && cart.items().length) this.step.set('details');
  }
  applyCoupon(): void {
    this.couponStatus.set(this.couponCode.trim() ? 'El cupón se validará antes del pago.' : 'Introduce un código para comprobarlo.');
  }

  isPackOpen(productId: string): boolean {
    return this.expandedPackIds().has(productId);
  }

  togglePack(product: Product): void {
    if (this.isPackOpen(product.id)) {
      this.expandedPackIds.update((open) => {
        const next = new Set(open);
        next.delete(product.id);
        return next;
      });
      return;
    }

    this.expandedPackIds.update((open) => new Set(open).add(product.id));
    const current = this.packContents()[product.slug];
    if (current?.loading || (current && !current.error)) return;

    this.packContents.update((state) => ({
      ...state,
      [product.slug]: { loading: true, error: null, items: current?.items ?? [] }
    }));
    this.catalog.getPackComponents(product.slug).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (items) => this.packContents.update((state) => ({
        ...state,
        [product.slug]: { loading: false, error: null, items }
      })),
      error: () => this.packContents.update((state) => ({
        ...state,
        [product.slug]: { loading: false, error: 'No hemos podido cargar el contenido del pack.', items: [] }
      }))
    });
  }
}
