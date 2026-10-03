import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { bottleImage } from './product-visual';
import { previewProducts, previewCategories } from './preview-catalog';
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
  primaryImageUrl: string | null;
}

interface CategoryListItemDto {
  id: string;
  name: string;
  slug: string;
  parentCategoryId: string | null;
}

interface PagedProductsDto {
  items: ProductListItemDto[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly productsSignal = signal<Product[]>([]);
  private readonly categoriesSignal = signal<Category[]>([]);
  private readonly loadingSignal = signal(true);
  private readonly errorSignal = signal<string | null>(null);

  readonly products = this.productsSignal.asReadonly();
  readonly categories = this.categoriesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  constructor() {
    this.refresh();
  }

  getProduct(slug: string): Product | undefined {
    return this.productsSignal().find((product) => product.slug === slug);
  }

  getProductBySlug(slug: string): Observable<Product | null> {
    if (environment.mockCatalog) return of(this.getProduct(slug) ?? null);
    return this.http.get<ProductListItemDto>(`${environment.apiBaseUrl}/api/v1/products/${encodeURIComponent(slug)}`).pipe(
      map((item) => this.toProduct(item)),
      catchError((error: HttpErrorResponse) => {
        if (error.status === 404) {
          return of(null);
        }

        return throwError(() => error);
      })
    );
  }

  getFeatured(): Product[] {
    return this.productsSignal().filter((product) => product.featured);
  }

  refresh(): void {
    if (environment.mockCatalog) {
      this.productsSignal.set(previewProducts);
      this.categoriesSignal.set(previewCategories);
      this.errorSignal.set(null);
      this.loadingSignal.set(false);
      return;
    }
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    const params = new HttpParams().set('page', '1').set('pageSize', '100');
    const products$ = this.http.get<PagedProductsDto>(`${environment.apiBaseUrl}/api/v1/products`, { params });
    const categories$ = this.http.get<CategoryListItemDto[]>(`${environment.apiBaseUrl}/api/v1/categories`);

    forkJoin({ products: products$, categories: categories$ }).subscribe({
      next: ({ products, categories }) => {
        this.productsSignal.set((products.items ?? []).map((item) => this.toProduct(item)));
        this.categoriesSignal.set((categories ?? []).map((category) => ({
          id: category.id,
          name: category.name,
          slug: category.slug,
          description: ''
        })));
        this.loadingSignal.set(false);
      },
      error: () => {
        this.productsSignal.set([]);
        this.categoriesSignal.set([]);
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
      image: item.primaryImageUrl ? this.mediaUrl(item.primaryImageUrl) : bottleImage(item),
      accent: '#8f3541',
      featured: true
    };
  }

  private mediaUrl(path: string): string {
    if (path.startsWith('http://') || path.startsWith('https://')) {
      return path;
    }

    const base = environment.apiBaseUrl.replace(/\/$/, '');
    return `${base}${path.startsWith('/') ? path : `/${path}`}`;
  }
}
