import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VisitService } from '../../core/visit.service';

@Component({ selector: 'ds-visits', standalone: true, imports: [FormsModule], template: `
  <section class="visits-hero"><div class="shell visits-hero-content"><p class="eyebrow light">La experiencia Dominio</p><h1>La Sierra<br><em>se vive aquí.</em></h1><p>Entra en una bodega pequeña, prueba vinos con nombre propio y descubre por qué la Rufete solo podía nacer en este paisaje.</p><a href="#reservar" class="button button-light">Quiero reservar <span>↓</span></a></div><div class="real-location-map visits-map" role="button" tabindex="0" (click)="mapOpen.set(true)" (keydown.enter)="mapOpen.set(true)" (keydown.space)="mapOpen.set(true); $event.preventDefault()" aria-label="Ampliar mapa de San Esteban de la Sierra"><img src="https://static-maps.yandex.ru/1.x/?lang=es_ES&amp;ll=-5.905,40.506&amp;z=16&amp;l=sat,skl&amp;size=650,450" alt="Vista aérea real de San Esteban de la Sierra"><span class="map-pin-ui" aria-hidden="true"><i></i></span><div class="map-card-label"><span>40° 30' N · 5° 54' W</span><strong>San Esteban de la Sierra</strong><small>Haz clic para ampliar ↗</small></div></div></section>
  @if (mapOpen()) { <div class="map-viewer-backdrop" (click)="mapOpen.set(false)"><section class="map-viewer" role="dialog" aria-modal="true" aria-label="Ubicación de Dominio de la Sierra" (click)="$event.stopPropagation()"><button class="map-viewer-close" type="button" (click)="mapOpen.set(false)" aria-label="Cerrar mapa">×</button><div class="map-viewer-image"><img src="https://static-maps.yandex.ru/1.x/?lang=es_ES&amp;ll=-5.905,40.506&amp;z=16&amp;l=sat,skl&amp;size=650,450" alt="Vista aérea ampliada de San Esteban de la Sierra"><span class="map-pin-ui" aria-hidden="true"><i></i></span></div><div class="map-viewer-copy"><span>40° 30' N · 5° 54' W</span><h2>San Esteban de la Sierra</h2><p>Dominio de la Sierra · Sierra de Francia · Salamanca</p><a class="button button-dark" href="https://www.google.com/maps/search/?api=1&amp;query=40.506,-5.905" target="_blank" rel="noopener">Ir a la dirección <span>↗</span></a></div></section></div> }
  <section class="visit-promise shell"><div><span>01</span><strong>Viñedos de montaña</strong></div><div><span>02</span><strong>Grupos reducidos</strong></div><div><span>03</span><strong>Cata guiada</strong></div></section>
  <section id="reservar" class="booking-section"><div class="reservation-cellar" aria-hidden="true"><img src="/assets/brand/reservation-bottle-web.png" alt=""><img src="/assets/brand/reservation-bottle-web.png" alt=""><img src="/assets/brand/reservation-bottle-web.png" alt=""><span><b>Dominio</b><em>de la Sierra</em><small>Rufete</small></span></div><div class="reservation-vine" aria-hidden="true"></div><div class="shell booking-layout section-pad"><div class="booking-intro"><div class="section-kicker">Reserva tu visita</div><h2>Elige tu manera<br>de conocer <em>Dominio.</em></h2><p>Dos recorridos, una misma idea: entender el vino desde la tierra. Selecciona la experiencia y prepara tu visita.</p><div class="visit-types"><button [class.selected]="visitType() === 'esencial'" (click)="setVisitType('esencial')"><span>01</span><strong>Visita esencial</strong><small>Bodega + cata de cuatro vinos</small><b>18 €</b></button><button [class.selected]="visitType() === 'profunda'" (click)="setVisitType('profunda')"><span>02</span><strong>La Sierra por dentro</strong><small>Viñedo + bodega + maridaje</small><b>32 €</b></button></div></div><div class="booking-card"><div class="booking-card-head"><p class="eyebrow">Tu experiencia</p><strong>{{ visitType() === 'esencial' ? 'Visita esencial' : 'La Sierra por dentro' }}</strong><small>{{ people() }} personas · {{ total() }} €</small></div><p class="demo-note">Disponibilidad de demostración · El pago se activará al conectar el TPV.</p>
    <div class="booking-step"><span>01</span><div class="custom-select"><label>Fecha</label><button type="button" class="select-trigger" (click)="toggleSelect('date')" [attr.aria-expanded]="openSelect() === 'date'">{{ selectedDateLabel() }} <i>⌄</i></button>@if (openSelect() === 'date') { <div class="calendar-popover"><div class="calendar-head"><button type="button" (click)="changeMonth(-1)" [disabled]="!canGoPrevious()" aria-label="Mes anterior">←</button><strong>{{ monthLabel() }}</strong><button type="button" (click)="changeMonth(1)" aria-label="Mes siguiente">→</button></div><div class="calendar-week"><span>L</span><span>M</span><span>X</span><span>J</span><span>V</span><span>S</span><span>D</span></div><div class="calendar-grid">@for (day of calendarDays(); track day.key) { <button type="button" [class.outside]="!day.currentMonth" [class.active]="date() === day.iso" [disabled]="!day.available" (click)="chooseDate(day.iso)" [attr.aria-label]="day.label">{{ day.number }}</button> }</div><p class="calendar-note"><span></span> Fechas disponibles durante los próximos cuatro meses</p></div> }</div></div>
    <div class="booking-step"><span>02</span><div class="custom-select"><label>Hora</label><button type="button" class="select-trigger" [disabled]="!date()" (click)="toggleSelect('slot')" [attr.aria-expanded]="openSelect() === 'slot'">{{ slot() || (date() ? 'Selecciona una hora' : 'Selecciona primero la fecha') }} <i>⌄</i></button>@if (openSelect() === 'slot') { <div class="select-menu select-menu-compact">@for (hour of slots(); track hour) { <button type="button" [class.active]="slot() === hour" (click)="chooseSlot(hour)">{{ hour }}</button> }</div> }</div></div>
    <div class="booking-step"><span>03</span><div class="custom-select"><label>Personas</label><button type="button" class="select-trigger" (click)="toggleSelect('people')" [attr.aria-expanded]="openSelect() === 'people'">{{ people() }} personas <i>⌄</i></button>@if (openSelect() === 'people') { <div class="select-menu select-menu-compact">@for (amount of peopleOptions; track amount) { <button type="button" [class.active]="people() === amount" (click)="choosePeople(amount)">{{ amount }} personas</button> }</div> }</div></div>
    <label class="privacy-check"><input type="checkbox" [checked]="privacyAccepted()" (change)="privacyAccepted.set(!privacyAccepted())"><span>Acepto la <a href="https://dominiodelasierra.com/politica-privacidad/" target="_blank" rel="noopener">política de privacidad</a> y el tratamiento de los datos de esta solicitud.</span></label><div class="booking-summary"><span>Total estimado<small>{{ people() }} × {{ visitType() === 'esencial' ? '18' : '32' }} €</small></span><strong>{{ total() }} €</strong></div>@if (confirmed) { <div class="booking-success" role="status"><strong>Tu visita ya tiene forma.</strong><span>Esta es una simulación: no se ha enviado ninguna solicitud ni realizado un cobro.</span></div> } @else { <button class="button button-dark full-width" [disabled]="!date() || !slot() || !privacyAccepted()" (click)="confirmBooking()">Revisar mi reserva <span>↗</span></button> }</div></div></section>
  <section class="visit-detail-band"><div class="vine-decoration vine-top" aria-hidden="true"></div><div class="vine-decoration vine-bottom" aria-hidden="true"></div><div class="shell"><div><p class="eyebrow">Lo que vas a encontrar</p><h2>Noventa minutos.<br><em>Una historia que queda.</em></h2><p class="detail-intro">Una visita sin prisas, pensada para entender el territorio con los cinco sentidos.</p></div><div class="detail-list"><p><span>01</span><strong>Caminar</strong> entre viñas viejas y bancales de piedra.</p><p><span>02</span><strong>Descubrir</strong> el método ancestral desde dentro.</p><p><span>03</span><strong>Sentarse</strong> a la mesa. Probar. Conversar.</p></div></div></section>
`, styles: [] })
export class VisitsComponent {
  readonly visitService = inject(VisitService);
  readonly visitType = signal('esencial');
  readonly date = signal('');
  readonly slot = signal('');
  readonly people = signal(2);
  readonly privacyAccepted = signal(false);
  readonly mapOpen = signal(false);
  readonly viewMonth = signal(this.firstAvailableMonth());
  readonly openSelect = signal<'date' | 'slot' | 'people' | null>(null);
  readonly peopleOptions = [2, 3, 4, 5, 6];
  confirmed = false;
  readonly slots = computed(() => this.visitService.availability.find((day) => day.date === this.date())?.slots ?? []);
  readonly total = computed(() => (this.visitType() === 'esencial' ? 18 : 32) * this.people());
  readonly selectedDateLabel = computed(() => this.date() ? `${this.shortDate(this.date())}, ${this.longDate(this.date())}` : 'Selecciona un día');
  readonly monthLabel = computed(() => new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' }).format(this.viewMonth()));
  readonly canGoPrevious = computed(() => {
    const first = this.firstAvailableMonth();
    const current = this.viewMonth();
    return current.getFullYear() > first.getFullYear() || current.getMonth() > first.getMonth();
  });
  readonly calendarDays = computed(() => {
    const month = this.viewMonth();
    const first = new Date(month.getFullYear(), month.getMonth(), 1, 12);
    const offset = (first.getDay() + 6) % 7;
    const availableDates = new Set(this.visitService.availability.map((day) => day.date));
    return Array.from({ length: 42 }, (_, index) => {
      const value = new Date(first);
      value.setDate(1 - offset + index);
      const iso = this.toIsoDate(value);
      return { key: iso, iso, number: value.getDate(), currentMonth: value.getMonth() === month.getMonth(), available: availableDates.has(iso), label: this.fullDate(iso) };
    });
  });
  setVisitType(type: string): void { this.visitType.set(type); this.confirmed = false; }
  toggleSelect(select: 'date' | 'slot' | 'people'): void { this.openSelect.set(this.openSelect() === select ? null : select); }
  chooseDate(date: string): void { this.date.set(date); this.slot.set(''); this.openSelect.set(null); this.confirmed = false; }
  chooseSlot(slot: string): void { this.slot.set(slot); this.openSelect.set(null); this.confirmed = false; }
  choosePeople(people: number): void { this.people.set(people); this.openSelect.set(null); this.confirmed = false; }
  changeMonth(delta: number): void { if (delta < 0 && !this.canGoPrevious()) return; const value = this.viewMonth(); this.viewMonth.set(new Date(value.getFullYear(), value.getMonth() + delta, 1, 12)); }
  shortDate(date: string): string { return new Intl.DateTimeFormat('es-ES', { weekday: 'short', day: 'numeric' }).format(new Date(`${date}T12:00:00`)); }
  longDate(date: string): string { return new Intl.DateTimeFormat('es-ES', { month: 'long' }).format(new Date(`${date}T12:00:00`)); }
  fullDate(date: string): string { return new Intl.DateTimeFormat('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(`${date}T12:00:00`)); }
  confirmBooking(): void { this.visitService.book({ visitType: this.visitType(), date: this.date(), slot: this.slot(), people: this.people() }); this.confirmed = true; }
  @HostListener('document:keydown.escape') closeMap(): void { this.mapOpen.set(false); }
  private firstAvailableMonth(): Date { const first = new Date(`${this.visitService.availability[0].date}T12:00:00`); return new Date(first.getFullYear(), first.getMonth(), 1, 12); }
  private toIsoDate(date: Date): string { return `${date.getFullYear()}-${`${date.getMonth() + 1}`.padStart(2, '0')}-${`${date.getDate()}`.padStart(2, '0')}`; }
}
