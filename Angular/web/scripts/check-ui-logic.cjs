/* Lightweight regression tests for local behaviour. Not a substitute for browser QA. */
const { readFileSync, existsSync } = require('node:fs');
const { resolve } = require('node:path');
const { runInNewContext } = require('node:vm');
const { strict: assert } = require('node:assert');
const ts = require('typescript');
const signal = initial => {
  let value = initial;
  const get = () => value;
  get.set = next => { value = next; };
  get.update = fn => { value = fn(value); };
  get.asReadonly = () => get;
  return get;
};
let injection;
const angular = { signal, input: signal, computed: fn => fn, effect: fn => fn(), inject: () => injection,
  Component: () => target => target, Injectable: () => target => target,
  Directive: () => target => target, HostListener: () => () => {}, output: () => ({ emit() {} }) };
function load(path, modules = {}, context = {}) {
  const source = readFileSync(resolve(__dirname, '..', path), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true } }).outputText;
  const exports = {};
  runInNewContext(js, { exports, require: name => modules[name] ?? (name === '@angular/core' ? angular : {}), Date, Intl, Set, Array, Math, window: { setTimeout() {} }, ...context });
  return exports;
}
const { CartService } = load('src/app/core/cart.service.ts');
const cart = new CartService();
const p = { id: 'a', slug: 'dominium-crianza', name: 'Dominium Crianza', reference: 'DS-DOM', price: 19.5 };
cart.add(p); cart.add(p); cart.add({ ...p, id: 'b', price: 10 });
assert.equal(cart.itemCount(), 3); assert.equal(cart.subtotal(), 49);
cart.update('a', 4); assert.equal(cart.itemCount(), 5);
cart.update('a', 0); assert.equal(cart.items().length, 1);
cart.remove('b'); assert.equal(cart.itemCount(), 0);
const { bottleImage, basketProducts, basketLayout } = load('src/app/core/product-visual.ts');
assert.equal(basketProducts([]).length, 0);
assert.equal(basketProducts([{ product:p, quantity:9 }]).length, 6);
assert.equal(basketProducts([{ product:p, quantity:9 }, { product:{ ...p, id:'b' }, quantity:1 }])[1].id, 'b');
assert.equal(basketProducts([{ product:p, quantity:2 }, { product:{ ...p, id:'b' }, quantity:2 }]).length, 4);
assert.ok(bottleImage(p).endsWith('dominium-cart.webp'));
assert.ok(bottleImage({ ...p, slug: 'blanco', name: '', reference: '' }).endsWith('blanco-cart.webp'));
assert.ok(bottleImage({ ...p, slug: 'ancestral', name: '', reference: '' }).endsWith('ancestral-cart.webp'));
assert.equal(basketLayout(0).length, 0); assert.equal(basketLayout(NaN).length, 0);
assert.equal(basketLayout(30).length, 6); assert.equal(basketLayout(-3).length, 0);
assert.equal(basketLayout(1)[0].x, 50); assert.equal(basketLayout(1)[0].tilt, 0);
assert.ok(basketLayout(1)[0].height > basketLayout(6)[0].height);
assert.equal(basketLayout(1)[0].height, 64);
assert.ok(basketLayout(2).every(slot => slot.height <= 64));
assert.ok(basketLayout(3).every(slot => slot.height <= 60));
assert.equal(basketLayout(2)[0].x, 40); assert.equal(basketLayout(2)[1].x, 60);
assert.equal(basketLayout(2)[0].tilt, -basketLayout(2)[1].tilt);
for (let count = 1; count <= 6; count++) {
  const slots = basketLayout(count);
  assert.equal(slots.length, count);
  assert.equal(slots.reduce((sum, slot) => sum + slot.x, 0) / count, 50);
  assert.ok(slots.every(slot => slot.x >= 27 && slot.x <= 73 && Math.abs(slot.tilt) <= 5));
}
const { bottleFlightFrames } = load('src/app/core/cart-motion.ts', { './product-visual':{ bottleImage } });
const frames = bottleFlightFrames(300, -200, .33, 4);
assert.equal(frames[0].opacity, 1); assert.equal(frames.at(-1).opacity, 0);
assert.ok(frames.at(-1).transform.includes('translate(300px,-200px)'));
assert.ok(frames.every((frame,index) => index === 0 || frame.offset > frames[index-1].offset));
const seatedFrames = bottleFlightFrames(300, -200, .45, 0, .7, 76);
assert.equal(seatedFrames[0].clipPath, 'inset(0 0 0% 0)');
assert.ok(parseFloat(seatedFrames.at(-1).clipPath.split(' ')[2]) > 29);
assert.ok(parseFloat(seatedFrames.at(-2).clipPath.split(' ')[2]) < parseFloat(seatedFrames.at(-1).clipPath.split(' ')[2]));
const { previewProducts } = load('src/app/core/preview-catalog.ts', { './product-visual': { bottleImage } });
assert.equal(previewProducts.length, 4);
for (const product of previewProducts) assert.ok(existsSync(resolve(__dirname, '../src', product.image.slice(1))));
injection = { nativeElement: { contains: node => node === 'inside' } };
const { DismissOutsideDirective } = load('src/app/shared/dismiss-outside.directive.ts');
const dismissal = new DismissOutsideDirective();
let dismissed = 0;
dismissal.dsDismissOutside.emit = () => dismissed++;
dismissal.onClick({ composedPath: () => [injection.nativeElement] }); assert.equal(dismissed, 0);
dismissal.onClick({ composedPath: () => [] }); assert.equal(dismissed, 1);
dismissal.onFocus({ target: 'inside' }); assert.equal(dismissed, 1);
dismissal.onFocus({ target: 'outside' }); assert.equal(dismissed, 2);
dismissal.onEscape(); assert.equal(dismissed, 3);
const { VisitService } = load('src/app/core/visit.service.ts');
injection = new VisitService();
assert.equal(injection.availability.length, 120);
assert.ok(injection.availability.every(day => day.slots.length > 0));
const { VisitsComponent } = load('src/app/features/visits/visits.component.ts');
const visits = new VisitsComponent();
visits.toggleSelect('date'); assert.equal(visits.openSelect(), 'date');
visits.chooseDate(injection.availability[0].date); assert.equal(visits.openSelect(), null);
visits.chooseSlot(visits.slots()[0]); visits.choosePeople(4); assert.equal(visits.total(), 72);
visits.setVisitType('profunda'); assert.equal(visits.total(), 128);
visits.toggleSelect('people'); visits.closeMap(); assert.equal(visits.openSelect(), null);
assert.equal(visits.calendarDays().length, 42);
const originalMonth = visits.monthLabel(); visits.changeMonth(1); assert.notEqual(visits.monthLabel(), originalMonth);
let bookingCalls = 0;
injection.book = value => { bookingCalls++; return value; };
visits.chooseLargeGroup();
assert.equal(visits.largeGroup(), true); assert.equal(visits.openSelect(), null);
assert.equal(visits.total(), null);
visits.date.set(''); visits.slot.set(''); visits.privacyAccepted.set(false);
assert.equal(visits.canReviewBooking(), true);
visits.confirmBooking();
assert.equal(visits.groupContactOpen(), true); assert.equal(bookingCalls, 0); assert.equal(visits.confirmed, false);
visits.groupContactOpen.set(false);
visits.choosePeople(3);
assert.equal(visits.largeGroup(), false); assert.equal(visits.total(), 96);
assert.equal(visits.canReviewBooking(), false);
visits.confirmBooking(); assert.equal(bookingCalls, 0);
visits.chooseDate(injection.availability[0].date); visits.chooseSlot(visits.slots()[0]); visits.privacyAccepted.set(true);
visits.confirmBooking(); assert.equal(bookingCalls, 1); assert.equal(visits.confirmed, true);
const groupContact = load('src/app/core/group-visit-contact.ts');
const contact = { name:'Ana García', email:'ana@example.com', phone:'+34 600 123 456', approximatePeople:12, privacy:true };
assert.equal(groupContact.validGroupContact(contact), true);
for (const change of [
  { name:'   ' }, { email:'no-es-correo' }, { email:'ana\r\n@example.com' }, { phone:'abc1234567' },
  { phone:'123' }, { phone:'1234567890123456' }, { approximatePeople:6 }, { approximatePeople:7.5 },
  { approximatePeople:NaN }, { approximatePeople:null }, { approximatePeople:'12' }, { privacy:false }
]) {
  assert.equal(groupContact.validGroupContact({...contact,...change}), false);
  assert.equal(groupContact.groupContactMailto({...contact,...change}, {experience:'Visita esencial'}), '');
}
const groupLink = new URL(groupContact.groupContactMailto(contact, {experience:'La Sierra por dentro',preferredDate:'2026-10-20',preferredTime:'12:30'}));
assert.equal(groupLink.protocol, 'mailto:'); assert.equal(groupLink.pathname, groupContact.GROUP_CONTACT_EMAIL);
assert.ok(groupLink.searchParams.get('body').includes('Nombre: Ana García'));
assert.ok(groupLink.searchParams.get('body').includes('Personas aproximadas: 12'));
assert.ok(groupLink.searchParams.get('body').includes('Correo de contacto: ana@example.com'));
assert.ok(groupLink.searchParams.get('body').includes('Teléfono: +34 600 123 456'));
assert.ok(groupLink.searchParams.get('body').includes('2026-10-20'));
assert.ok(groupLink.searchParams.get('body').includes('12:30'));
const noDateLink = new URL(groupContact.groupContactMailto(contact, {experience:'Visita esencial',preferredTime:'12:30'}));
assert.ok(!noDateLink.searchParams.get('body').includes('Fecha orientativa'));
assert.ok(!noDateLink.searchParams.get('body').includes('Hora orientativa'));
const groupSource = readFileSync(resolve(__dirname, '../src/app/features/visits/group-visit-contact.component.ts'), 'utf8');
assert.ok(groupSource.includes('dsModalFocus'));
assert.ok(groupSource.includes('aria-modal="true"'));
assert.ok(groupSource.includes("document:keydown.escape"));
assert.ok(groupSource.includes('form.control.markAllAsTouched()'));
assert.ok(!groupSource.includes('localStorage'));
assert.ok(groupSource.includes('No se ha enviado automáticamente'));
const { GroupVisitContactComponent } = load('src/app/features/visits/group-visit-contact.component.ts', {'../../core/group-visit-contact':groupContact});
const groupModal = new GroupVisitContactComponent();
let markedInvalid = 0, closedGroup = 0;
const formDouble = {invalid:false,control:{markAllAsTouched(){markedInvalid++;}}};
groupModal.prepare(formDouble); assert.equal(groupModal.draft(), ''); assert.equal(markedInvalid, 1);
groupModal.form = {...contact};
groupModal.prepare(formDouble); assert.ok(groupModal.draft().startsWith('mailto:'));
groupModal.form.email = 'correo-no-valido'; groupModal.invalidateDraft();
assert.equal(groupModal.draft(), ''); groupModal.prepare(formDouble);
assert.equal(groupModal.draft(), ''); assert.equal(markedInvalid, 2);
groupModal.closed.emit = () => closedGroup++; groupModal.close(); assert.equal(closedGroup, 1);
injection = { availability: [] }; assert.doesNotThrow(() => new VisitsComponent());
injection = { products: () => [] };
const { AdminDataService } = load('src/app/features/admin/admin-data.service.ts');
const data = new AdminDataService();
const { AdminDashboardComponent } = load('src/app/features/admin/admin-dashboard.component.ts');
const admin = new AdminDashboardComponent(data, { paramMap: { subscribe(fn) { fn({ get: () => null }); return { unsubscribe() {} }; } } }, { navigate() {} });
admin.orderQuery = 'Carlos'; assert.ok(admin.filteredOrders().every(o => o.customer.includes('Carlos')));
admin.orderQuery = ''; admin.orderMin = 100; assert.ok(admin.filteredOrders().every(o => o.total >= 100));
admin.orderMin = null; admin.orderFilter = 'paid'; assert.ok(admin.filteredOrders().every(o => o.status === 'paid'));
admin.orderFilter = 'all'; admin.orderFrom = '2026-09-29'; assert.ok(admin.filteredOrders().every(o => o.isoDate >= admin.orderFrom));
admin.productStatus = 'inactive'; assert.ok(admin.filteredProducts().every(p => !p.active));
admin.stockQuery = 'no-existe'; assert.equal(admin.filteredStock().length, 0);
admin.customerType = 'Restaurante'; assert.ok(admin.filteredCustomers().every(c => c.type === 'Restaurante'));
admin.invoiceStatus = 'draft'; assert.ok(admin.filteredInvoices().every(i => i.status === 'draft'));
admin.financeRange = 'quarter'; assert.equal(admin.financePoints().length, 3);
admin.financeRange = 'month'; assert.equal(admin.financePoints().length, 1);
assert.equal(admin.financeTotals().revenue, data.sales().at(-1).revenue);
const stocked = data.products().find(p => p.reserved > 0);
data.adjustStock(stocked.id, -1000); assert.equal(data.products().find(p => p.id === stocked.id).stock, stocked.reserved);
assert.equal(admin.nextActions({ payment: 'pending', status: 'paid' }).length, 0);
const visitSource = readFileSync(resolve(__dirname, '../src/app/features/visits/visits.component.ts'), 'utf8');
for (const key of ['date', 'slot', 'people']) assert.ok(visitSource.includes(`openSelect() === '${key}' && openSelect.set(null)`));
const storage = new Map();
const { CookieConsentService, parseConsent, CONSENT_KEY } = load('src/app/core/cookie-consent.service.ts', {}, {
  localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) }
});
assert.equal(parseConsent('broken'), null);
assert.equal(parseConsent(null), null);
assert.equal(parseConsent(JSON.stringify({version:1,maps:'true',savedAt:Date.now()})), null);
assert.equal(parseConsent(JSON.stringify({version:1,maps:true,savedAt:0})), null);
assert.equal(parseConsent(JSON.stringify({version:1,maps:true,savedAt:Date.now()+60000})), null);
assert.equal(parseConsent(JSON.stringify({version:2,maps:true,savedAt:Date.now()})), null);
const privacy = new CookieConsentService();
assert.equal(privacy.mapsAllowed(), false); assert.equal(privacy.needsChoice(), true);
privacy.save(false); assert.equal(privacy.needsChoice(), false); assert.equal(privacy.mapsAllowed(), false);
assert.equal(new CookieConsentService().needsChoice(), false);
privacy.openSettings(); assert.equal(privacy.needsChoice(), true);
privacy.save(true); assert.equal(new CookieConsentService().mapsAllowed(), true);
privacy.save(false); assert.equal(privacy.mapsAllowed(), false);
assert.equal(parseConsent(storage.get(CONSENT_KEY)).maps, false);
const { CookieConsentService: BlockedConsent } = load('src/app/core/cookie-consent.service.ts', {}, {
  localStorage: { getItem() { throw Error('blocked'); }, setItem() { throw Error('blocked'); } }
});
const blockedPrivacy = new BlockedConsent(); blockedPrivacy.save(true);
assert.equal(blockedPrivacy.mapsAllowed(), true); assert.equal(blockedPrivacy.storageFailed(), true);
const mapSource = readFileSync(resolve(__dirname, '../src/app/shared/location-map.component.ts'), 'utf8');
assert.ok(mapSource.includes('consent.mapsAllowed() && viewerOpen()'));
const brandStyles = readFileSync(resolve(__dirname, '../src/brand.scss'), 'utf8');
assert.ok(brandStyles.includes('grid-template-columns:minmax(0,1fr) minmax(0,520px)'));
assert.ok(brandStyles.includes('.story-band > .story-copy > * { grid-column:1; }'));
assert.ok(brandStyles.includes('.story-band > .story-copy {'));
const basketSource = readFileSync(resolve(__dirname, '../src/app/shared/harvest-basket.component.ts'), 'utf8');
assert.ok(basketSource.includes('[class.is-receiving]="receivingIndex() === index"'));
assert.ok(basketSource.includes('[style.clip-path]="frontClip"'));
assert.ok(basketSource.includes('aspect-ratio:1/3'));
assert.ok(basketSource.includes('.is-sparse .harvest-slot'));
assert.ok(basketSource.includes('.is-sparse .harvest-slot { max-height:56px; }'));
assert.ok(!basketSource.includes('height:85px'));
assert.ok(!readFileSync(resolve(__dirname, '../src/styles.scss'), 'utf8').includes('.cart-link span {'));
const emblem = readFileSync(resolve(__dirname, '../src/assets/brand/dominio-emblem.svg'), 'utf8');
assert.ok(emblem.includes('viewBox="0 0 104 96"'));
assert.ok(!emblem.includes('<text'));
const favicon = readFileSync(resolve(__dirname, '../src/favicon.svg'), 'utf8');
assert.ok(favicon.includes('viewBox="6 6 100 82"'));
const lightEmblem = readFileSync(resolve(__dirname, '../src/assets/brand/dominio-emblem-light.svg'), 'utf8');
assert.equal(lightEmblem.replaceAll('#F3E9D8', '#512638'), emblem);
for (const svg of [emblem, lightEmblem, favicon]) {
  assert.ok(!svg.includes('<circle')); assert.ok(!svg.includes('<rect'));
  assert.ok(svg.includes('id="letter-s"'));
}
for (const svg of [emblem, lightEmblem]) {
  assert.ok(!svg.includes('<mask')); assert.ok(!svg.includes('stroke='));
  assert.ok(svg.includes('transform="translate(-8 0)"'));
}
assert.ok(favicon.includes('mask="url(#ds-weave)"'));
assert.ok(!brandStyles.includes('.brand-mark::after{content:\'\';position:absolute;inset:4px;border:1px solid currentColor'));
assert.ok(readFileSync(resolve(__dirname, '../src/styles.scss'), 'utf8').includes('.brand-mark { border: 0; border-radius: 0; background: transparent;'));
assert.ok(favicon.includes('prefers-color-scheme:dark'));
assert.ok(readFileSync(resolve(__dirname, '../src/index.html'), 'utf8').includes('dominio-emblem-light.svg'));
console.log('OK: carrito, imágenes locales, selectores, calendario, filtros CRM, finanzas, stock y consentimiento (aceptar/rechazar, persistencia, caducidad y almacenamiento bloqueado).');
