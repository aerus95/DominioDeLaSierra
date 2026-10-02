import { LegalComponent } from './features/public/legal.component';
import { Routes } from '@angular/router';
import { HomeComponent } from './features/public/home.component';
import { StoryComponent } from './features/public/story.component';
import { ContactComponent } from './features/public/contact.component';
import { ShopComponent } from './features/catalog/shop.component';
import { ProductDetailComponent } from './features/catalog/product-detail.component';
import { CartComponent } from './features/cart/cart.component';
import { VisitsComponent } from './features/visits/visits.component';
import { AdminDashboardComponent } from './features/admin/admin-dashboard.component';

export const appRoutes: Routes = [
  { path: '', component: HomeComponent, title: 'Dominio de la Sierra · Vinos de Rufete' },
  { path: 'vinos', component: ShopComponent, title: 'Nuestros vinos · Dominio de la Sierra' },
  { path: 'vinos/:slug', component: ProductDetailComponent },
  { path: 'visitas', component: VisitsComponent, title: 'Visita la bodega · Dominio de la Sierra' },
  { path: 'historia', component: StoryComponent, title: 'Nuestra historia · Dominio de la Sierra' },
  { path: 'contacto', component: ContactComponent, title: 'Contacto · Dominio de la Sierra' },
  { path: 'carrito', component: CartComponent, title: 'Tu carrito · Dominio de la Sierra' },
  { path: 'gestion', component: AdminDashboardComponent, title: 'Gestión · Dominio de la Sierra' },
  { path: 'gestion/:section', component: AdminDashboardComponent, title: 'Gestión · Dominio de la Sierra' },
  { path: 'aviso-legal', component: LegalComponent, data: { legalKey: 'aviso-legal' }, title: 'Información · Dominio de la Sierra' },
  { path: 'privacidad', component: LegalComponent, data: { legalKey: 'privacidad' }, title: 'Información · Dominio de la Sierra' },
  { path: 'cookies', component: LegalComponent, data: { legalKey: 'cookies' }, title: 'Información · Dominio de la Sierra' },
  { path: 'condiciones-de-compra', component: LegalComponent, data: { legalKey: 'condiciones-de-compra' }, title: 'Información · Dominio de la Sierra' },
  { path: '**', redirectTo: '' }
];
