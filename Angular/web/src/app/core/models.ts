export interface Category {
  id: string;
  name: string;
  slug: string;
  description: string;
}

export interface Product {
  id: string;
  reference: string;
  name: string;
  slug: string;
  description: string;
  categoryId: string;
  categorySlug: string;
  categoryName: string;
  price: number;
  vintage: string;
  grape: string;
  alcohol: string;
  image: string;
  accent: string;
  featured?: boolean;
}

export interface CartItem {
  product: Product;
  quantity: number;
}

export interface VisitAvailability {
  date: string;
  slots: string[];
}

export interface VisitBooking {
  visitType: string;
  date: string;
  slot: string;
  people: number;
}
