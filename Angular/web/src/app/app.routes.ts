import { Routes } from '@angular/router';
import { HomeComponent } from './features/public/home.component';
import { StoryComponent } from './features/public/story.component';
import { ContactComponent } from './features/public/contact.component';
import { ShopComponent } from './features/catalog/shop.component';
import { ProductDetailComponent } from './features/catalog/product-detail.component';
import { CartComponent } from './features/cart/cart.component';
import { VisitsComponent } from './features/visits/visits.component';

export const appRoutes: Routes = [
  { path: '', component: HomeComponent, title: 'Dominio de la Sierra · Vinos de Rufete' },
  { path: 'vinos', component: ShopComponent, title: 'Nuestros vinos · Dominio de la Sierra' },
  { path: 'vinos/:slug', component: ProductDetailComponent },
  { path: 'visitas', component: VisitsComponent, title: 'Visita la bodega · Dominio de la Sierra' },
  { path: 'historia', component: StoryComponent, title: 'Nuestra historia · Dominio de la Sierra' },
  { path: 'contacto', component: ContactComponent, title: 'Contacto · Dominio de la Sierra' },
  { path: 'carrito', component: CartComponent, title: 'Tu carrito · Dominio de la Sierra' },
  { path: '**', redirectTo: '' }
];
