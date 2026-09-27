import { Category, Product, VisitAvailability } from './models';

export const categories: Category[] = [
  { id: 'tintos', name: 'Tintos', slug: 'tintos', description: 'Rufete, fruta y tiempo.' },
  { id: 'blancos', name: 'Blancos', slug: 'blancos', description: 'Frescura serrana en cada copa.' },
  { id: 'ancestral', name: 'Ancestrales', slug: 'ancestral', description: 'Burbuja, paciencia y origen.' },
  { id: 'packs', name: 'Packs de bodega', slug: 'packs', description: 'Para compartir la sierra.' }
];

export const products: Product[] = [
  {
    id: 'dominium-crianza', reference: 'DS-DOM-CR-21', name: 'Dominium Crianza', slug: 'dominium-crianza',
    description: 'Un tinto de parcela, profundo y elegante, que deja hablar al paisaje de la Sierra de Francia.',
    categoryId: 'tintos', categoryName: 'Tinto', price: 19.5, vintage: '2021', grape: 'Rufete', alcohol: '14% vol.',
    image: 'https://images.unsplash.com/photo-1510812431401-41d2bd2722f3?auto=format&fit=crop&w=900&q=85', accent: '#8f3541', featured: true
  },
  {
    id: 'momentum-roble', reference: 'DS-MOM-23', name: 'Momentum Roble', slug: 'momentum-roble',
    description: 'La entrada más amable a nuestro viñedo: fruta roja, madera medida y final fresco.',
    categoryId: 'tintos', categoryName: 'Tinto', price: 13.9, vintage: '2023', grape: 'Rufete', alcohol: '13,5% vol.',
    image: 'https://images.unsplash.com/photo-1473973266408-ed4e27abdd47?auto=format&fit=crop&w=900&q=85', accent: '#b26445', featured: true
  },
  {
    id: 'blanco-sierra', reference: 'DS-BLA-25', name: 'Dominio de la Sierra Blanco', slug: 'blanco-sierra',
    description: 'Un blanco de altura, mineral y luminoso, con una acidez que recuerda a la piedra húmeda.',
    categoryId: 'blancos', categoryName: 'Blanco', price: 15.5, vintage: '2025', grape: 'Viura', alcohol: '12,5% vol.',
    image: 'https://images.unsplash.com/photo-1556679343-c7306c1976bc?auto=format&fit=crop&w=900&q=85', accent: '#cda968', featured: true
  },
  {
    id: 'rufete-ancestral', reference: 'DS-RUF-AN', name: 'Rufete Ancestral', slug: 'rufete-ancestral',
    description: 'El método ancestral convertido en celebración: ligero, vivo y radicalmente de aquí.',
    categoryId: 'ancestral', categoryName: 'Ancestral', price: 21, vintage: '2024', grape: 'Rufete', alcohol: '11,5% vol.',
    image: '/assets/brand/rufete-ancestral-web.jpg', accent: '#d9b887', featured: true
  },
  {
    id: 'pack-descubrimiento', reference: 'DS-PACK-01', name: 'Pack Descubrimiento', slug: 'pack-descubrimiento',
    description: 'Cuatro botellas para recorrer la bodega desde casa: dos tintos, un blanco y un ancestral.',
    categoryId: 'packs', categoryName: 'Pack', price: 65, vintage: 'Selección', grape: 'Selección', alcohol: 'Variado',
    image: 'https://images.unsplash.com/photo-1535869462434-f92cc30bf40c?auto=format&fit=crop&w=900&q=85', accent: '#6f5962'
  },
  {
    id: 'seleccion-parcela', reference: 'DS-PACK-02', name: 'Selección de Parcela', slug: 'seleccion-parcela',
    description: 'Tres añadas especiales de nuestras parcelas más singulares, en una caja de edición limitada.',
    categoryId: 'packs', categoryName: 'Pack', price: 92, vintage: 'Edición limitada', grape: 'Rufete', alcohol: '14% vol.',
    image: 'https://images.unsplash.com/photo-1506377247377-2a5b3b417ebb?auto=format&fit=crop&w=900&q=85', accent: '#4b3033'
  }
];

export const visitAvailability: VisitAvailability[] = [
  { date: '2026-09-18', slots: ['11:00', '13:00', '17:30'] },
  { date: '2026-09-19', slots: ['11:00', '13:00', '17:30'] },
  { date: '2026-09-20', slots: ['11:00', '13:00'] },
  { date: '2026-09-25', slots: ['11:00', '17:30'] },
  { date: '2026-09-26', slots: ['11:00', '13:00', '17:30'] },
  { date: '2026-09-27', slots: ['11:00', '13:00'] }
];
