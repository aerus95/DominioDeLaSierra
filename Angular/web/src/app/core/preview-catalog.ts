import { Category, Product } from './models';
import { bottleImage } from './product-visual';

/** Explicit review-only fixtures. Never used as a silent API fallback. */
export const previewCategories: Category[] = [
  { id: 'tintos', slug: 'tintos', name: 'Tintos', description: 'Vinos tintos de la Sierra' },
  { id: 'blancos', slug: 'blancos', name: 'Blancos', description: 'Vinos blancos de la Sierra' },
  { id: 'ancestral', slug: 'ancestral', name: 'Ancestral', description: 'Método ancestral' }
];
export const previewProducts: Product[] = [
  { id:'preview-dominium', slug:'dominium-crianza', name:'Dominium Crianza', reference:'DS-DOM-21', categoryId:'tintos', categorySlug:'tintos', categoryName:'Tinto', price:19.5, vintage:'2021', grape:'Rufete', description:'La expresión de nuestra tierra, con el tiempo como aliado. Precio de demostración, pendiente de catálogo definitivo.', alcohol:'Pendiente de validar', accent:'#81364a', featured:true, image:'' },
  { id:'preview-momentum', slug:'momentum-roble', name:'Momentum Roble', reference:'DS-MOM-23', categoryId:'tintos', categorySlug:'tintos', categoryName:'Tinto', price:13.9, vintage:'2023', grape:'Rufete y Tempranillo', description:'Una invitación a descubrir el paisaje de la Sierra en la copa. Precio de demostración, pendiente de catálogo definitivo.', alcohol:'Pendiente de validar', accent:'#a66b4f', featured:true, image:'' },
  { id:'preview-blanco', slug:'blanco-sierra', name:'Dominio de la Sierra Blanco', reference:'DS-BLA-25', categoryId:'blancos', categorySlug:'blancos', categoryName:'Blanco', price:15.5, vintage:'2025', grape:'Multivarietal', description:'Nuestra mirada sobre las variedades blancas de la Sierra. Precio de demostración, pendiente de catálogo definitivo.', alcohol:'Pendiente de validar', accent:'#b9a270', featured:true, image:'' },
  { id:'preview-ancestral', slug:'rufete-ancestral', name:'Rufete Ancestral', reference:'DS-RUF-AN', categoryId:'ancestral', categorySlug:'ancestral', categoryName:'Ancestral', price:21, vintage:'Pendiente de validar', grape:'Rufete', description:'Un espumoso elaborado mediante el método ancestral. Precio de demostración, pendiente de catálogo definitivo.', alcohol:'Pendiente de validar', accent:'#bd986b', featured:true, image:'' }
].map(product => ({ ...product, image:bottleImage(product) }));
