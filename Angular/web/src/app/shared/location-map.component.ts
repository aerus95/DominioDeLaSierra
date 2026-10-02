import { Component, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { CookieConsentService } from '../core/cookie-consent.service';

@Component({
  selector: 'ds-location-map', standalone: true,
  template: `
    <button type="button" class="real-location-map location-map-trigger" (click)="open()" aria-label="Ampliar el mapa de San Esteban de la Sierra">
      <span class="location-map-photo"><img src="/assets/brand/sierra-aerial-esri.jpg" width="1600" height="1100" loading="lazy" alt="Vista aérea de San Esteban de la Sierra, sin etiquetas de calles"><span class="map-pin-ui" aria-hidden="true"><i></i></span><span class="map-source">Esri · Vantor · Earthstar Geographics · GIS User Community</span></span>
      <span class="location-map-caption"><small>Sierra de Francia · Salamanca</small><strong>San Esteban de la Sierra</strong><span>Ampliar mapa ↗</span></span>
    </button>
    <dialog #viewer class="location-dialog" aria-labelledby="location-title" (click)="closeOnBackdrop($event)" (close)="viewerOpen.set(false)">
      <div class="location-dialog-inner">
        <button class="map-viewer-close" type="button" (click)="viewer.close()" aria-label="Cerrar mapa">×</button>
        <div class="location-dialog-map">
          @if (consent.mapsAllowed() && viewerOpen()) {
            <iframe title="Mapa interactivo de San Esteban de la Sierra" src="https://maps.google.com/maps?q=San%20Esteban%20de%20la%20Sierra%2C%20Salamanca&amp;t=k&amp;z=16&amp;output=embed" referrerpolicy="no-referrer" allowfullscreen></iframe>
          } @else {
            <img src="/assets/brand/sierra-aerial-esri.jpg" alt="Vista aérea del municipio">
            <span class="map-source">Imagen: Esri, Vantor, Earthstar Geographics · GIS User Community</span>
            <div class="map-consent"><p>Explora el mapa con más detalle.</p><small>Al cargarlo autorizas Google Maps, que puede utilizar cookies y recibir tu IP. Guardaremos esta preferencia en tu navegador; puedes cambiarla desde el pie de página.</small><button type="button" class="button button-dark" (click)="consent.save(true)">Autorizar y cargar mapa ↗</button></div>
          }
        </div>
        <div class="location-dialog-copy"><p class="eyebrow">Nuestro origen</p><h2 id="location-title">San Esteban<br>de la Sierra</h2><p>Sierra de Francia · Salamanca. El mapa muestra el municipio; confirma con la bodega el punto de encuentro de tu visita.</p><a class="button button-dark" href="https://www.google.com/maps/search/?api=1&amp;query=Dominio+de+la+Sierra+San+Esteban+de+la+Sierra" target="_blank" rel="noopener">Ir a la dirección ↗</a></div>
      </div>
    </dialog>
  `
})
export class LocationMapComponent {
  @ViewChild('viewer', { static: true }) viewer!: ElementRef<HTMLDialogElement>;
  readonly consent = inject(CookieConsentService);
  readonly viewerOpen = signal(false);
  open(): void { this.viewerOpen.set(true); this.viewer.nativeElement.showModal(); }
  closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.viewer.nativeElement) this.viewer.nativeElement.close();
  }
}
