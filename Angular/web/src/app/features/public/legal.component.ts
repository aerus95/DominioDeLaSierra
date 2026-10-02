import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { legalPages } from './legal-content';

@Component({ selector: 'ds-legal', standalone: true, imports: [RouterLink], template: `
  <section class="shell legal-page">
    <p class="eyebrow">Dominio de la Sierra · Información</p>
    <h1>{{ page().title }}</h1><p class="legal-intro">{{ page().intro }}</p>
    <nav aria-label="Información legal"><a routerLink="/aviso-legal">Aviso legal</a><a routerLink="/privacidad">Privacidad</a><a routerLink="/cookies">Cookies</a><a routerLink="/condiciones-de-compra">Compras y envíos</a></nav>
    <p class="legal-review" role="note">Documento de revisión · 30 de septiembre de 2026. Requiere validación de la titular y revisión jurídica antes de activar ventas o tratamientos reales.</p>
    @for (section of page().sections; track section.title) { <article><h2>{{ section.title }}</h2><p>{{ section.text }}</p></article> }
    <a class="underline-link" routerLink="/contacto">¿Tienes una consulta? Escríbenos ↗</a>
  </section>
` })
export class LegalComponent {
  private readonly route = inject(ActivatedRoute);
  readonly page = toSignal(this.route.data.pipe(map(data => legalPages[data['legalKey']] ?? legalPages['aviso-legal'])), { initialValue: legalPages['aviso-legal'] });
}
