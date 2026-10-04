import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { bottleImage } from './product-visual';
import { previewProducts, previewCategories } from './preview-catalog';
import { Category, PackComponent, Product, ProductKindName } from './models';

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
  kind: ProductKindName;
}

interface PackComponentDto {
  productId: string;
  name: string;
  description: string;
  primaryImageUrl: string | null;
  quantity: number;
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

const catalogError = 'No hemos podido cargar el catálogo desde la bodega. Inténtalo de nuevo.';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  static readonly pageSizes = [12, 24, 48] as const;
  static readonly defaultPageSize = 12;

  private readonly http = inject(HttpClient);
  private readonly productsSignal = signal<Product[]>([]);
  private readonly pageSignal = signal(1);
  private readonly pageSizeSignal = signal(CatalogService.defaultPageSize);
  private readonly totalItemsSignal = signal(0);
  private readonly totalPagesSignal = signal(0);
  private readonly categoriesSignal = signal<Category[]>([]);
  private readonly loadingSignal = signal(true);
  private readonly errorSignal = signal<string | null>(null);
  private readonly selectionSignal = signal<Product[]>([]);
  private readonly selectionLoadingSignal = signal(true);
  private readonly selectionErrorSignal = signal<string | null>(null);
  private catalogRequest = 0;
  private readonly packComponentsCache = new Map<string, PackComponent[]>();
  private readonly packComponentsRequests = new Map<string, Observable<PackComponent[]>>();

  readonly items = this.productsSignal.asReadonly();
  readonly products = this.items;
  readonly page = this.pageSignal.asReadonly();
  readonly pageSize = this.pageSizeSignal.asReadonly();
  readonly totalItems = this.totalItemsSignal.asReadonly();
  readonly totalPages = this.totalPagesSignal.asReadonly();
  readonly categories = this.categoriesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly selection = this.selectionSignal.asReadonly();
  readonly selectionLoading = this.selectionLoadingSignal.asReadonly();
  readonly selectionError = this.selectionErrorSignal.asReadonly();

  constructor() {
    this.loadCategories();
    this.loadSelection();
  }

  loadPage(page: number, pageSize: number, category: string | null): void {
    const size = this.normalizePageSize(pageSize);
    const requestedPage = page < 1 ? 1 : page;
    const request = ++this.catalogRequest;
    this.pageSignal.set(requestedPage);
    this.pageSizeSignal.set(size);
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    if (environment.mockCatalog) {
      this.applyPreviewPage(requestedPage, size, category, request);
      return;
    }

    let params = new HttpParams().set('page', String(requestedPage)).set('pageSize', String(size));
    if (category) params = params.set('category', category);
    this.http.get<PagedProductsDto>(`${environment.apiBaseUrl}/api/v1/products`, { params }).subscribe({
      next: (result) => this.applyPage(result, request),
      error: () => {
        if (request !== this.catalogRequest) return;
        this.productsSignal.set([]);
        this.totalItemsSignal.set(0);
        this.totalPagesSignal.set(0);
        this.errorSignal.set(catalogError);
        this.loadingSignal.set(false);
      }
    });
  }

  getPackComponents(slug: string): Observable<PackComponent[]> {
    const cached = this.packComponentsCache.get(slug);
    if (cached) return of(cached);
    const pending = this.packComponentsRequests.get(slug);
    if (pending) return pending;

    const request = this.loadPackComponents(slug).pipe(
      tap((items) => this.packComponentsCache.set(slug, items)),
      finalize(() => this.packComponentsRequests.delete(slug)),
      shareReplay(1)
    );
    this.packComponentsRequests.set(slug, request);
    return request;
  }

  getProductBySlug(slug: string): Observable<Product | null> {
    if (environment.mockCatalog) return of(previewProducts.find((product) => product.slug === slug) ?? null);
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

  private loadSelection(): void {
    if (environment.mockCatalog) {
      this.selectionSignal.set(previewProducts.slice(0, CatalogService.defaultPageSize));
      this.selectionErrorSignal.set(null);
      this.selectionLoadingSignal.set(false);
      return;
    }

    const params = new HttpParams().set('page', '1').set('pageSize', String(CatalogService.defaultPageSize));
    this.http.get<PagedProductsDto>(`${environment.apiBaseUrl}/api/v1/products`, { params }).subscribe({
      next: (result) => {
        this.selectionSignal.set((result.items ?? []).map((item) => this.toProduct(item)));
        this.selectionErrorSignal.set(null);
        this.selectionLoadingSignal.set(false);
      },
      error: () => {
        this.selectionSignal.set([]);
        this.selectionErrorSignal.set(catalogError);
        this.selectionLoadingSignal.set(false);
      }
    });
  }

  private loadCategories(): void {
    if (environment.mockCatalog) {
      this.categoriesSignal.set(previewCategories);
      return;
    }

    this.http.get<CategoryListItemDto[]>(`${environment.apiBaseUrl}/api/v1/categories`).subscribe({
      next: (categories) => this.categoriesSignal.set((categories ?? []).map((category) => ({
        id: category.id,
        name: category.name,
        slug: category.slug,
        description: ''
      }))),
      error: () => this.categoriesSignal.set([])
    });
  }

  private applyPage(result: PagedProductsDto, request: number): void {
    if (request !== this.catalogRequest) return;
    this.productsSignal.set((result.items ?? []).map((item) => this.toProduct(item)));
    this.pageSignal.set(result.page);
    this.pageSizeSignal.set(result.pageSize);
    this.totalItemsSignal.set(result.totalItems);
    this.totalPagesSignal.set(result.totalPages);
    this.loadingSignal.set(false);
  }

  private applyPreviewPage(page: number, pageSize: number, category: string | null, request: number): void {
    if (request !== this.catalogRequest) return;
    const filtered = category ? previewProducts.filter((product) => product.categorySlug === category) : previewProducts;
    const totalItems = filtered.length;
    const totalPages = totalItems === 0 ? 0 : Math.ceil(totalItems / pageSize);
    const safePage = totalPages === 0 ? 1 : Math.min(page, totalPages);
    const start = (safePage - 1) * pageSize;
    this.productsSignal.set(filtered.slice(start, start + pageSize));
    this.pageSignal.set(safePage);
    this.pageSizeSignal.set(pageSize);
    this.totalItemsSignal.set(totalItems);
    this.totalPagesSignal.set(totalPages);
    this.loadingSignal.set(false);
  }

  private normalizePageSize(pageSize: number): number {
    return (CatalogService.pageSizes as readonly number[]).includes(pageSize)
      ? pageSize
      : CatalogService.defaultPageSize;
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
      kind: item.kind,
      vintage: '',
      grape: '',
      alcohol: '',
      image: item.primaryImageUrl ? this.mediaUrl(item.primaryImageUrl) : bottleImage(item),
      accent: '#8f3541',
      featured: true
    };
  }

  private loadPackComponents(slug: string): Observable<PackComponent[]> {
    if (environment.mockCatalog) return of([]);
    return this.http
      .get<PackComponentDto[]>(`${environment.apiBaseUrl}/api/v1/products/${encodeURIComponent(slug)}/components`)
      .pipe(map((items) => (items ?? []).map((item) => this.toPackComponent(item))));
  }

  private toPackComponent(item: PackComponentDto): PackComponent {
    return {
      productId: item.productId,
      name: item.name,
      description: item.description,
      image: item.primaryImageUrl ? this.mediaUrl(item.primaryImageUrl) : null,
      quantity: item.quantity
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
