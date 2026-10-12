import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CheckoutCustomerInput,
  CheckoutLineInput,
  CheckoutOutcome,
  CheckoutPayment,
  CheckoutQuote,
  CheckoutSession,
  GuestOrder,
  checkoutAllowedByServer,
  checkoutUsesPanelCookie,
  checkoutOutcomePayload,
  paymentSessionPayload,
  placePayload,
  quotePayload
} from './checkout';

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);
  private readonly withCredentials = checkoutUsesPanelCookie(environment.apiBaseUrl);

  availability(): Observable<boolean> {
    return this.http.get<unknown>(`${environment.apiBaseUrl}/api/v1/checkout/availability`, this.options()).pipe(
      map((body) => checkoutAllowedByServer(body)),
      catchError(() => of(false))
    );
  }

  quote(lines: readonly CheckoutLineInput[], postalCode: string): Observable<CheckoutQuote> {
    return this.http.post<CheckoutQuote>(
      `${environment.apiBaseUrl}/api/v1/checkout/quotes`,
      quotePayload(lines, postalCode),
      this.options());
  }

  place(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[], idempotencyKey: string): Observable<GuestOrder> {
    return this.http.post<GuestOrder>(
      `${environment.apiBaseUrl}/api/v1/checkout/orders`,
      placePayload(customer, lines),
      this.options({ 'Idempotency-Key': idempotencyKey }));
  }

  pay(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[], idempotencyKey: string): Observable<CheckoutPayment> {
    return this.http.post<CheckoutPayment>(
      `${environment.apiBaseUrl}/api/v1/checkout/payments`,
      placePayload(customer, lines),
      this.options({ 'Idempotency-Key': idempotencyKey }));
  }

  startPayment(orderId: string, checkoutAccessToken: string): Observable<CheckoutSession> {
    return this.http.post<CheckoutSession>(
      `${environment.apiBaseUrl}/api/v1/checkout/payment-sessions`,
      paymentSessionPayload(orderId),
      this.options({ 'Checkout-Access-Token': checkoutAccessToken }));
  }

  outcome(orderId: string, checkoutAccessToken: string): Observable<CheckoutOutcome> {
    return this.http.post<CheckoutOutcome>(
      `${environment.apiBaseUrl}/api/v1/checkout/outcome`,
      checkoutOutcomePayload(orderId),
      this.options({ 'Checkout-Access-Token': checkoutAccessToken }));
  }

  private options(headers?: Record<string, string>) {
    return { headers, withCredentials: this.withCredentials };
  }
}
