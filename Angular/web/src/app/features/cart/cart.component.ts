import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { CheckoutCustomerInput, CheckoutQuote, FREE_SHIPPING_EUROS, GuestOrder, HOLD_RELEASED_MESSAGE, checkoutErrorMessage, checkoutSignature, eurosFromCents, forgetIdempotencyKey, readCheckoutAccess, rememberCheckoutAccess, resolveIdempotencyKey, stripeCheckoutUrl } from '../../core/checkout';
import { CartService } from '../../core/cart.service';
import { CatalogService } from '../../core/catalog.service';
import { CheckoutService } from '../../core/checkout.service';
import { CartItem, PackComponent, Product } from '../../core/models';
import { packQuantityLabel } from '../../core/pack-contents';
import { categoryGrapeLabel } from '../../core/wine-display';

interface PackContentsState {
  loading: boolean;
  error: string | null;
  items: PackComponent[];
}

@Component({ selector: 'ds-cart', standalone: true, imports: [RouterLink, DecimalPipe, FormsModule], template: `
  <section class="cart-hero"><div class="cart-hero-image"><img [src]="cartVisual()" alt=""></div><div class="cart-hero-overlay"></div><div class="shell"><p class="eyebrow light">Tu selección</p><h1>Botellas con<br><em>un lugar detrás.</em></h1><div class="cart-hero-meta"><span>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'botella' : 'botellas' }}</span><span>Envío peninsular</span><span>Compra segura</span></div><div class="cart-journey" aria-label="Proceso de compra"><button [class.active]="step() === 'selection'" [class.done]="step() !== 'selection'" (click)="step.set('selection')"><i>01</i><span>Tu selección</span><small>Vinos y cantidades</small></button><button [disabled]="!cart.items().length" [class.active]="step() === 'details'" [class.done]="step() === 'review'" (click)="step.set('details')"><i>02</i><span>Datos de envío</span><small>Destino del pedido</small></button><button [disabled]="step() !== 'review'" [class.active]="step() === 'review'" [class.done]="paying()"><i>03</i><span>Revisión y pago</span><small>Stripe en pruebas</small></button></div></div></section>
  <section class="shell cart-layout section-pad" [attr.aria-busy]="quoting() || placing() || paying()"><div class="cart-harvest" aria-hidden="true"></div>@if (step() === 'pending' && placed(); as placedOrder) { <div class="checkout-review"><p class="eyebrow">Pedido reservado</p><h2>Tu pedido está<br><em>pendiente de pago.</em></h2><p role="status">El pedido sigue pendiente. El pago se abre en Stripe en modo de pruebas y esta pantalla no confirma el cobro.</p><div class="review-address"><span>Número</span><strong>{{ placedOrder.number }}</strong><p>Total {{ euros(placedOrder.totalCents) | number:'1.2-2' }} € · IVA de productos incluido</p><small>Subtotal {{ euros(placedOrder.productSubtotalCents) | number:'1.2-2' }} € · Envío {{ shippingLabel(placedOrder.shippingCents) }} · Reservado hasta {{ expiry(placedOrder.reservationExpiresAt) }}</small></div>@if (placedOrder.shippingVatPending && placedOrder.shippingCents > 0) { <p class="checkout-note">El envío son 8 € brutos. Su desglose fiscal está pendiente de confirmación.</p> }@if (checkoutError()) { <p class="checkout-error" role="alert">{{ checkoutError() }}</p> }<button class="button button-dark" type="button" (click)="payWithStripe()" [disabled]="paying()">{{ paying() ? 'Abriendo Stripe…' : 'Pagar con Stripe' }}</button><button class="button button-dark" type="button" (click)="step.set('selection')">Volver a mi caja</button></div> } @else if (cart.items().length) {
    @if (step() === 'selection') { <div class="cart-lines"><div class="cart-lines-head"><h2>Tu caja</h2><a routerLink="/vinos">Seguir descubriendo ↗</a></div>@for (item of cart.items(); track item.product.id) { <article class="cart-line"><div class="cart-line-image"><img [src]="item.product.image" [alt]="item.product.name"><span>{{ item.product.vintage }}</span></div><div><p class="product-ref">{{ categoryGrapeLabel(item.product.categoryName, item.product.grape) }}</p><h3>{{ item.product.name }}</h3><button class="remove-button" (click)="cart.remove(item.product.id)">Eliminar de la caja</button></div><div class="quantity"><button (click)="cart.update(item.product.id, item.quantity - 1)" aria-label="Restar una unidad">−</button><span>{{ item.quantity }}</span><button (click)="cart.update(item.product.id, item.quantity + 1)" aria-label="Sumar una unidad">+</button></div><strong>{{ lineTotal(item) | number:'1.2-2' }} €</strong>@if (item.product.kind === 'Pack') { <div class="pack-panel"><button type="button" class="pack-toggle" (click)="togglePack(item.product)" [attr.aria-expanded]="isPackOpen(item.product.id)">{{ isPackOpen(item.product.id) ? 'Ver menos' : 'Ver más' }}</button>@if (isPackOpen(item.product.id)) { <div class="pack-contents">@if (packContents()[item.product.slug]; as contents) { @if (contents.loading) { <p class="pack-contents-note">Cargando el contenido del pack.</p> } @else if (contents.error) { <p class="pack-contents-note">{{ contents.error }}</p> } @else if (contents.items.length === 0) { <p class="pack-contents-note">Este pack no detalla su contenido.</p> } @else { @for (component of contents.items; track component.productId) { <article class="pack-component">@if (component.image) { <img class="pack-component-image" [src]="component.image" [alt]="component.name"> } @else { <span class="pack-component-placeholder" aria-hidden="true"></span> }<div><h4>{{ component.name }}</h4><p>{{ component.description }}</p><small>{{ packQuantityLabel(component.quantity, item.quantity) }}</small></div></article> } } } @else { <p class="pack-contents-note">Cargando el contenido del pack.</p> }</div> }</div> }</article> }<div class="cart-trust"><span>Embalaje protegido</span><span>Atención directa de bodega</span><span>Vino enviado desde origen</span></div></div><aside class="cart-summary"><p class="eyebrow">Resumen de la caja</p><div><span>Subtotal</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><div><span>Envío</span><span>Se calcula con tu dirección · gratis desde {{ freeShippingEuros }} €</span></div><div class="coupon-box"><label for="coupon-selection">¿Tienes un cupón?</label><div><input id="coupon-selection" [(ngModel)]="couponCode" placeholder="Código"><button type="button" (click)="applyCoupon()">Aplicar</button></div>@if (couponStatus()) { <small>{{ couponStatus() }}</small> }</div><hr><div class="summary-total"><span>Total estimado</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><button class="button button-dark full-width" (click)="step.set('details')">Continuar con mis datos <span>↗</span></button><small>El presupuesto no reserva stock. El pago abre Stripe en modo de pruebas.</small><blockquote>“De la Sierra de Francia a tu mesa.”</blockquote></aside> }
    @if (step() === 'details') { <div class="checkout-form-panel"><button class="checkout-back" (click)="step.set('selection')">← Volver a mi caja</button><p class="eyebrow">02 · Datos de envío</p><h2>El próximo destino<br><em>de estas botellas.</em></h2><p>Necesitamos lo justo para que el vino llegue bien y podamos avisarte durante el recorrido.</p><form #shippingForm="ngForm" (ngSubmit)="submitDetails(shippingForm.valid)"><fieldset class="shipping-block"><legend><i>01</i><span><strong>Persona de contacto</strong><small>Para confirmar y seguir el pedido</small></span></legend><div class="form-row"><label>Nombre y apellidos<input name="name" [(ngModel)]="customer.name" autocomplete="name" required placeholder="Nombre de quien recibe"></label><label>Correo electrónico<input name="email" [(ngModel)]="customer.email" type="email" autocomplete="email" required placeholder="nombre@correo.com"></label></div><label>Teléfono<input name="phone" [(ngModel)]="customer.phone" type="tel" autocomplete="tel" required placeholder="+34 600 000 000"></label></fieldset><fieldset class="shipping-block"><legend><i>02</i><span><strong>Dirección de entrega</strong><small>Envíos disponibles en Península</small></span></legend><label>Dirección<input name="address" [(ngModel)]="customer.address" autocomplete="street-address" required placeholder="Calle, número, piso y puerta"></label><div class="shipping-grid"><label>Código postal<input name="postal" [(ngModel)]="customer.postal" inputmode="numeric" autocomplete="postal-code" pattern="[0-9]{5}" maxlength="5" required placeholder="00000"></label><label>Localidad<input name="city" [(ngModel)]="customer.city" autocomplete="address-level2" required></label><label>Provincia<input name="province" [(ngModel)]="customer.province" autocomplete="address-level1" required></label></div><label>Indicaciones para la entrega <input name="notes" [(ngModel)]="customer.notes" placeholder="Opcional · horario, portal o referencia"></label></fieldset><label class="privacy-check"><input type="checkbox" name="privacy" [(ngModel)]="customer.privacy" required><span>Acepto la <a routerLink="/privacidad">política de privacidad</a> para preparar este pedido.</span></label>@if (checkoutError() && step() === 'details') { <p class="checkout-error" role="alert">{{ checkoutError() }}</p> }<button class="button button-dark checkout-submit" [disabled]="shippingForm.invalid || quoting()">{{ quoting() ? 'Calculando el presupuesto…' : 'Revisar mi pedido' }} <span>↗</span></button></form></div><aside class="cart-summary checkout-mini-summary"><div class="summary-title"><p class="eyebrow">Tu caja</p><button type="button" (click)="step.set('selection')">Editar selección</button></div><div class="mini-cart-products">@for (item of cart.items(); track item.product.id) { <article><img [src]="item.product.image" alt=""><span><strong>{{ item.product.name }}</strong><small>{{ item.product.price | number:'1.2-2' }} €</small></span><div class="quantity compact"><button type="button" (click)="cart.update(item.product.id, item.quantity - 1)" aria-label="Restar una unidad">−</button><span>{{ item.quantity }}</span><button type="button" (click)="cart.update(item.product.id, item.quantity + 1)" aria-label="Sumar una unidad">+</button></div></article> }</div><div class="coupon-box"><label for="coupon-details">Cupón</label><div><input id="coupon-details" [(ngModel)]="couponCode" placeholder="Código promocional"><button type="button" (click)="applyCoupon()">Aplicar</button></div>@if (couponStatus()) { <small>{{ couponStatus() }}</small> }</div><hr><div class="summary-total"><span>Subtotal</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><small>Podrás comprobar todo antes del pago.</small></aside> }
    @if (step() === 'review') { <div class="checkout-review"><button class="checkout-back" (click)="step.set('details')">← Editar mis datos</button><p class="eyebrow">03 · Revisión</p><h2>Todo listo para<br><em>pagar.</em></h2><div class="review-address"><span>Entrega</span><strong>{{ customer.name }}</strong><p>{{ customer.address }} · {{ customer.postal }} {{ customer.city }} · {{ customer.province }}</p><small>{{ customer.email }} · {{ customer.phone }}</small></div><div class="review-products">@for (item of cart.items(); track item.product.id) { <div><img [src]="item.product.image" alt=""><span>{{ item.quantity }} × {{ item.product.name }}</span><strong>{{ lineTotal(item) | number:'1.2-2' }} €</strong></div> }</div></div><aside class="cart-summary payment-summary"><p class="eyebrow">Total del pedido</p>@if (quote(); as current) { <div><span>Subtotal</span><strong>{{ euros(current.productSubtotalCents) | number:'1.2-2' }} €</strong></div><div><span>IVA incluido</span><strong>{{ euros(current.productVatCents) | number:'1.2-2' }} €</strong></div><div><span>Envío</span><strong>{{ current.shippingCents === 0 ? 'Gratis' : (euros(current.shippingCents) | number:'1.2-2') + ' €' }}</strong></div>@if (current.shippingVatPending && current.shippingCents > 0) { <p class="checkout-note">Los precios son PVP con IVA incluido. El envío son 8 € brutos; su tipo impositivo está pendiente de confirmación.</p> }<hr><div class="summary-total"><span>Total</span><strong>{{ euros(current.totalCents) | number:'1.2-2' }} €</strong></div>@if (checkoutError()) { <p class="checkout-error" role="alert">{{ checkoutError() }}</p> }<button class="button button-dark full-width" type="button" (click)="placeOrder()" [disabled]="paying()">{{ paying() ? 'Abriendo Stripe…' : 'Pagar con Stripe' }}</button><small>Pagar recalcula el precio, reserva el stock y abre Stripe. Esta pantalla no confirma el cobro.</small> } @else { <p role="status">{{ quoting() ? 'Calculando el presupuesto…' : 'Calcula el presupuesto con tu dirección.' }}</p> }</aside> }
  } @else { <div class="empty-cart"><span class="empty-glyph">DS</span><p class="eyebrow">Tu caja espera</p><h2>Aún no has elegido<br><em>tu primer vino.</em></h2><p>Recorre nuestra colección y encuentra la botella que te lleve de vuelta a la Sierra.</p><a routerLink="/vinos" class="button button-dark">Descubrir los vinos <span>↗</span></a><div class="empty-cart-notes"><span>Rufete autóctona</span><span>8.000 botellas al año</span><span>Desde San Esteban</span></div></div> }</section>
`, styles: [`.checkout-error{margin:0 0 1rem;color:#8a2b3b}.checkout-note{display:block;margin:.35rem 0 1rem;color:#6d5c63;font-size:.85rem}`] })
export class CartComponent {
  readonly packQuantityLabel = packQuantityLabel;
  readonly categoryGrapeLabel = categoryGrapeLabel;
  readonly freeShippingEuros = FREE_SHIPPING_EUROS;
  readonly step = signal<'selection' | 'details' | 'review' | 'pending'>('selection');
  readonly couponStatus = signal('');
  readonly quote = signal<CheckoutQuote | null>(null);
  readonly quoting = signal(false);
  readonly placing = signal(false);
  readonly paying = signal(false);
  readonly checkoutError = signal('');
  readonly placed = signal<GuestOrder | null>(null);
  private readonly expandedPackIds = signal<ReadonlySet<string>>(new Set());
  readonly packContents = signal<Record<string, PackContentsState>>({});
  private readonly catalog = inject(CatalogService);
  private readonly checkout = inject(CheckoutService);
  private readonly destroyRef = inject(DestroyRef);
  private quotedSignature = '';
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

  euros(cents: number): number {
    return eurosFromCents(cents);
  }

  shippingLabel(cents: number): string {
    return cents === 0 ? 'gratuito' : `${eurosFromCents(cents).toFixed(2).replace('.', ',')} €`;
  }

  expiry(value: string): string {
    return new Intl.DateTimeFormat('es-ES', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
  }

  lineTotal(item: CartItem): number {
    const quoted = this.quote()?.lines.find((line) => line.productId === item.product.id);
    return quoted ? eurosFromCents(quoted.lineTotalCents) : item.product.price * item.quantity;
  }

  submitDetails(valid: boolean | null): void {
    if (!valid || this.quoting()) return;
    const lines = this.cart.items().map((item) => ({ productId: item.product.id, quantity: item.quantity }));
    this.checkoutError.set('');
    this.quoting.set(true);
    this.checkout.quote(lines, this.customer.postal).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (quote) => {
        this.quote.set(quote);
        this.quotedSignature = this.orderSignature();
        this.quoting.set(false);
        this.step.set('review');
      },
      error: (error: HttpErrorResponse) => {
        this.quoting.set(false);
        this.checkoutError.set(checkoutErrorMessage(error.error, 'No hemos podido calcular el presupuesto.'));
      }
    });
  }

  placeOrder(): void {
    if (this.paying()) return;
    const signature = this.orderSignature();
    if (signature !== this.quotedSignature) {
      this.checkoutError.set('La caja o la dirección han cambiado. Calcula de nuevo el presupuesto.');
      this.step.set('details');
      return;
    }

    const lines = this.cart.items().map((item) => ({ productId: item.product.id, quantity: item.quantity }));
    const storage = this.checkoutStorage();
    const key = resolveIdempotencyKey(signature, storage);
    this.checkoutError.set('');
    this.paying.set(true);
    this.checkout.pay(this.customerInput(), lines, key).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (payment) => {
        rememberCheckoutAccess(payment.orderId, payment.checkoutAccessToken, storage);
        if (payment.status === 'Paid') {
          this.paying.set(false);
          this.checkoutError.set('El navegador no puede dar el pago por confirmado.');
          return;
        }
        const target = stripeCheckoutUrl(payment.checkoutUrl);
        if (!target) {
          this.paying.set(false);
          this.checkoutError.set('La pasarela no ha devuelto una dirección de pago válida.');
          return;
        }
        window.location.assign(target);
      },
      error: (error: HttpErrorResponse) => {
        this.paying.set(false);
        const message = checkoutErrorMessage(error.error, 'No hemos podido abrir el pago. La caja sigue igual.');
        if (message === HOLD_RELEASED_MESSAGE) forgetIdempotencyKey(signature, storage);
        this.checkoutError.set(message);
      }
    });
  }

  payWithStripe(): void {
    const order = this.placed();
    if (!order || this.paying()) return;
    const token = readCheckoutAccess(order.orderId, this.checkoutStorage());
    if (!token) {
      this.checkoutError.set('No encontramos el acceso a este pago. Vuelve a confirmar el pedido.');
      return;
    }

    this.checkoutError.set('');
    this.paying.set(true);
    this.checkout.startPayment(order.orderId, token).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (session) => {
        const target = stripeCheckoutUrl(session.checkoutUrl);
        if (!target) {
          this.paying.set(false);
          this.checkoutError.set('La pasarela no ha devuelto una dirección de pago válida.');
          return;
        }
        window.location.assign(target);
      },
      error: (error: HttpErrorResponse) => {
        this.paying.set(false);
        this.checkoutError.set(checkoutErrorMessage(error.error, 'No hemos podido abrir el pago. El pedido sigue pendiente.'));
      }
    });
  }

  private customerInput(): CheckoutCustomerInput {
    return {
      name: this.customer.name,
      email: this.customer.email,
      phone: this.customer.phone,
      address: this.customer.address,
      postalCode: this.customer.postal,
      city: this.customer.city,
      province: this.customer.province,
      notes: this.customer.notes
    };
  }

  private orderSignature(): string {
    return checkoutSignature(
      this.customerInput(),
      this.cart.items().map((item) => ({ productId: item.product.id, quantity: item.quantity }))
    );
  }

  private checkoutStorage(): Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> | null {
    try {
      return sessionStorage;
    } catch {
      return null;
    }
  }
}
