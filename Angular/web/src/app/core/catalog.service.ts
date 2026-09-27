import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { environment } from '../../environments/environment';
import { Category, Product } from './models';

interface ProductListItemDto {
  id: string;
  reference: string;
  name: string;
  slug: string;
  description: string;
  price: number;
  vatRate: number;
  categoryId: string;
  categoryName: string;
  categorySlug: string;
}

interface PagedProductsDto {
  items: ProductListItemDto[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

const fallbackImage = '/assets/brand/rufete-vineyard-v2.png';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly productsSignal = signal<Product[]>([]);
  private readonly loadingSignal = signal(true);
  private readonly errorSignal = signal<string | null>(null);

  readonly products = this.productsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly categories = computed(() => this.uniqueCategories(this.productsSignal()));

  constructor() {
    this.refresh();
  }

  getProduct(slug: string): Product | undefined {
    return this.productsSignal().find((product) => product.slug === slug);
  }

  getFeatured(): Product[] {
    return this.productsSignal().filter((product) => product.featured);
  }

  refresh(): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    const params = new HttpParams().set('page', '1').set('pageSize', '100');

    this.http.get<PagedProductsDto>(`${environment.apiBaseUrl}/api/v1/products`, { params }).subscribe({
      next: (page) => {
        this.productsSignal.set((page.items ?? []).map((item) => this.toProduct(item)));
        this.loadingSignal.set(false);
      },
      error: () => {
        this.productsSignal.set([]);
        this.errorSignal.set('No hemos podido cargar el catálogo desde la bodega. Inténtalo de nuevo.');
        this.loadingSignal.set(false);
      }
    });
  }

  private toProduct(item: ProductListItemDto): Product {
    return {
      id: item.id,
      reference: item.reference,
      name: item.name,
      slug: item.slug,
      description: item.description,
      categoryId: item.categoryId,
      categorySlug: item.categorySlug,
      categoryName: item.categoryName,
      price: item.price,
      vintage: '',
      grape: '',
      alcohol: '',
      image: fallbackImage,
      accent: '#8f3541',
      featured: true
    };
  }

  private uniqueCategories(products: Product[]): Category[] {
    const seen = new Map<string, Category>();
    for (const product of products) {
      if (seen.has(product.categorySlug)) {
        continue;
      }
      seen.set(product.categorySlug, {
        id: product.categoryId,
        name: product.categoryName,
        slug: product.categorySlug,
        description: ''
      });
    }
    return [...seen.values()];
  }
}
