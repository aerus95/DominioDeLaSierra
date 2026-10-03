import { AfterViewInit, Component, OnDestroy, computed, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, Subscription } from 'rxjs';
import { basketProducts } from './core/product-visual';
import { CartService } from './core/cart.service';
import { environment } from '../environments/environment';
import { CookieConsentService } from './core/cookie-consent.service';
import { CookieConsentComponent } from './shared/cookie-consent.component';
import { HarvestBasketComponent } from './shared/harvest-basket.component';
import { CartFeedbackComponent } from './shared/cart-feedback.component';

@Component({
  selector: 'ds-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DecimalPipe, CookieConsentComponent, HarvestBasketComponent, CartFeedbackComponent],
  template: `
    <div [class.management-mode]="isManagementRoute()">
    @if (showAgeGate() && !isManagementRoute()) {
      <div class="age-gate" role="dialog" aria-modal="true" aria-labelledby="age-title">
        <div class="age-card">
          <p class="eyebrow">Dominio de la Sierra</p>
          <h1 id="age-title">El vino empieza en el paisaje.</h1>
          <p>{{ ageDenied() ? 'El acceso está reservado a mayores de 18 años. Puedes cerrar esta página.' : 'Bienvenido a Dominio de la Sierra. Confirma que tienes 18 años o más para continuar.' }}</p>
          <div class="age-actions">
            <button class="button button-dark" (click)="acceptAge()">Sí, soy mayor de edad</button>
            <button class="button button-quiet" (click)="ageDenied.set(true)">Soy menor de edad</button>
          </div>
        </div>
      </div>
    }

    <a class="skip-link" href="#contenido">Saltar al contenido</a>
    <header class="site-header" [attr.inert]="showAgeGate() && !isManagementRoute() ? '' : null">
      <div class="utility-bar"><span>{{ previewMode ? 'Vista de revisión · catálogo y precios de demostración' : 'Vinos de altura desde San Esteban de la Sierra' }}</span><span class="utility-origin">Rufete · Sierra de Francia · Salamanca</span><a class="utility-whatsapp" href="https://wa.me/34665144697" target="_blank" rel="noopener" aria-label="WhatsApp de la bodega: +34 665 144 697">WhatsApp · +34 665 144 697 ↗</a></div>
      <div class="nav-wrap shell">
        <button class="menu-toggle" [attr.aria-label]="menuOpen() ? 'Cerrar menú' : 'Abrir menú'" [attr.aria-expanded]="menuOpen()" (click)="menuOpen.set(!menuOpen())">☰</button>
        <a class="brand" routerLink="/" (click)="menuOpen.set(false)" aria-label="Dominio de la Sierra · Inicio"><span class="brand-mark brand-emblem"><img src="/assets/brand/dominio-emblem.svg" width="64" height="56" alt=""></span><span class="brand-wordmark"><span class="brand-name">Dominio<span class="brand-name-line"><i>de la</i> Sierra</span></span><small>Rufete · Sierra de Francia</small></span></a>
        <nav class="main-nav" [class.open]="menuOpen()" aria-label="Navegación principal">
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact: true}" (click)="menuOpen.set(false)">Inicio</a>
          <a routerLink="/vinos" routerLinkActive="active" (click)="menuOpen.set(false)">Nuestros vinos</a>
          <a routerLink="/visitas" routerLinkActive="active" (click)="menuOpen.set(false)">Visita la bodega</a>
          <a routerLink="/historia" routerLinkActive="active" (click)="menuOpen.set(false)">Nuestra historia</a>
          <a routerLink="/contacto" routerLinkActive="active" (click)="menuOpen.set(false)">Contacto</a>
        </nav>
        <div class="nav-actions"><a routerLink="/contacto" class="nav-icon" aria-label="Contacto">↗</a><div class="cart-nav"><a routerLink="/carrito" routerLinkActive="active" class="cart-link" [attr.aria-label]="'Ver cesta: ' + cart.itemCount() + ' productos'"><span class="cart-label">Carrito</span><ds-harvest-basket [products]="visibleCartBottles()" [count]="cart.itemCount()" /></a><div class="cart-preview"><div class="cart-preview-head"><span>Tu cesta</span><strong>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'producto' : 'productos' }}</strong></div>@if (cart.items().length) { <div class="cart-preview-products">@for (item of cart.items(); track item.product.id) { <div><img [src]="item.product.image" [alt]="item.product.name"><span><strong>{{ item.product.name }}</strong><small>{{ item.quantity }} × {{ item.product.price | number:'1.2-2' }} €</small></span></div> }</div><div class="shipping-meter"><span>@if (shippingRemaining() > 0) { Te faltan {{ shippingRemaining() | number:'1.2-2' }} € para el envío gratuito } @else { Ya tienes envío gratuito }</span><i><b [style.width.%]="shippingProgress()"></b></i></div><div class="cart-preview-total"><span>Subtotal</span><strong>{{ cart.subtotal() | number:'1.2-2' }} €</strong></div><div class="cart-preview-actions"><a routerLink="/carrito">Ver cesta</a><a routerLink="/carrito" [queryParams]="{ paso: 'datos' }">Finalizar compra</a></div> } @else { <div class="cart-preview-empty"><span>Tu primera botella empieza aquí.</span><a routerLink="/vinos">Descubrir los vinos ↗</a></div> }</div></div></div>
      </div>
    </header>
    <main id="contenido" [attr.inert]="showAgeGate() && !isManagementRoute() ? '' : null"><router-outlet /></main>
    <a class="whatsapp-float" href="https://wa.me/34665144697?text=Hola%2C%20tengo%20una%20consulta%20sobre%20Dominio%20de%20la%20Sierra" target="_blank" rel="noopener" aria-label="Consultar por WhatsApp"><svg aria-hidden="true" viewBox="0 0 32 32"><path d="M16 3a12.6 12.6 0 0 0-10.8 19l-1.5 5.4 5.6-1.5A12.7 12.7 0 1 0 16 3Zm0 22.9a10 10 0 0 1-5.1-1.4l-.4-.2-3.3.9.9-3.2-.2-.4A10.1 10.1 0 1 1 16 25.9Zm5.6-7.5c-.3-.2-1.8-.9-2.1-1-.3-.1-.5-.2-.7.2l-1 1.2c-.2.2-.4.2-.7.1a8.3 8.3 0 0 1-4.1-3.6c-.3-.5.3-.5.8-1.6.1-.2 0-.4 0-.6l-1-2.5c-.3-.6-.6-.5-.8-.5h-.7c-.2 0-.6.1-.9.4-.3.4-1.2 1.2-1.2 2.9 0 1.7 1.2 3.3 1.4 3.5.2.2 2.4 3.7 5.9 5.2 2.2.9 3.1 1 4.2.8.7-.1 1.8-.8 2.1-1.5.3-.8.3-1.4.2-1.5-.2-.3-.4-.4-.7-.5Z"/></svg><span>¿Hablamos?</span></a>
    <footer class="site-footer" [attr.inert]="showAgeGate() && !isManagementRoute() ? '' : null"><span class="footer-rufete" aria-hidden="true">Rufete</span><div class="footer-grape" aria-hidden="true"></div>
      <div class="shell footer-grid">
        <div><a class="brand footer-brand" routerLink="/" aria-label="Dominio de la Sierra · Inicio"><span class="brand-mark brand-emblem"><img src="/assets/brand/dominio-emblem-light.svg" width="64" height="56" alt=""></span><span class="brand-wordmark"><span class="brand-name">Dominio<span class="brand-name-line"><i>de la</i> Sierra</span></span><small>Rufete · Sierra de Francia</small></span></a><p>Una bodega pequeña, una variedad única y una forma de hacer vino que empieza por mirar el paisaje.</p></div>
        <div><p class="footer-label">Explora</p><a routerLink="/vinos">Nuestros vinos</a><a routerLink="/visitas">Visita la bodega</a><a routerLink="/historia">Nuestra historia</a></div>
        <div><p class="footer-label">Encuéntranos</p><p>San Esteban de la Sierra<br>Salamanca, España</p><a href="mailto:info@dominiodelasierra.com">info&#64;dominiodelasierra.com</a><div class="footer-origin"><span></span>40° 30' N · 5° 54' W</div></div>
      </div>
      <div class="shell footer-bottom"><span>© 2026 Bodegas Dominio de la Sierra S.L.</span><span><a routerLink="/aviso-legal">Aviso legal</a><a routerLink="/privacidad">Privacidad</a><a routerLink="/cookies">Cookies</a><button type="button" class="footer-privacy-settings" (click)="consent.openSettings()">Configurar cookies</button><a routerLink="/condiciones-de-compra">Envíos y compras</a></span></div>
    </footer>
    @if (!isManagementRoute() && !showAgeGate()) { <ds-cookie-consent /><ds-cart-feedback /> }
    </div>
  `
})
export class AppComponent implements AfterViewInit, OnDestroy {
  readonly previewMode = environment.mockCatalog;
  readonly menuOpen = signal(false);
  readonly ageDenied = signal(false);
  readonly showAgeGate = signal(this.readAgeGate());
  readonly isManagementRoute = signal(false);
  readonly visibleCartBottles = computed(() => basketProducts(this.cart.items()));
  readonly shippingRemaining = computed(() => Math.max(0, 60 - this.cart.subtotal()));
  readonly shippingProgress = computed(() => Math.min(100, this.cart.subtotal() / 60 * 100));
  private observer?: IntersectionObserver;
  private navigation?: Subscription;
  constructor(public readonly cart: CartService, private readonly router: Router, public readonly consent: CookieConsentService) {
    this.isManagementRoute.set(this.router.url.startsWith('/gestion'));
    this.navigation = this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd)).subscribe((event) => {
      this.isManagementRoute.set(event.urlAfterRedirects.startsWith('/gestion'));
      this.menuOpen.set(false);
      setTimeout(() => this.bindMotion(), 40);
    });
  }

  ngAfterViewInit(): void {
    if (!('IntersectionObserver' in window) || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    this.observer = new IntersectionObserver(entries => entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add('motion-visible');
        this.observer?.unobserve(entry.target);
      }
    }), { rootMargin: '0px 0px -10% 0px', threshold: .08 });
    this.bindMotion();
  }

  ngOnDestroy(): void { this.observer?.disconnect(); this.navigation?.unsubscribe(); }

  private bindMotion(): void {
    if (!this.observer || this.isManagementRoute()) return;
    document.querySelectorAll('main section:not(.home-hero):not(.visits-hero):not(.story-page-hero):not(.contact-hero), .product-card, .contact-channel').forEach((element) => {
      if (!element.classList.contains('motion-bound')) {
        element.classList.add('motion-bound');
        this.observer?.observe(element);
      }
    });
  }

  acceptAge(): void {
    this.showAgeGate.set(false);
    try { sessionStorage.setItem('ds-age-confirmed', 'true'); } catch { /* storage may be unavailable */ }
  }

  private readAgeGate(): boolean {
    try { return sessionStorage.getItem('ds-age-confirmed') !== 'true'; } catch { return true; }
  }
}
