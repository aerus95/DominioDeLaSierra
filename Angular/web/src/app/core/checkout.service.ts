import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CheckoutCustomerInput,
  CheckoutLineInput,
  CheckoutOutcome,
  CheckoutPayment,
  CheckoutQuote,
  CheckoutSession,
  GuestOrder,
  checkoutOutcomePayload,
  paymentSessionPayload,
  placePayload,
  quotePayload
} from './checkout';

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);

  quote(lines: readonly CheckoutLineInput[], postalCode: string): Observable<CheckoutQuote> {
    return this.http.post<CheckoutQuote>(`${environment.apiBaseUrl}/api/v1/checkout/quotes`, quotePayload(lines, postalCode));
  }

  place(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[], idempotencyKey: string): Observable<GuestOrder> {
    return this.http.post<GuestOrder>(
      `${environment.apiBaseUrl}/api/v1/checkout/orders`,
      placePayload(customer, lines),
      { headers: { 'Idempotency-Key': idempotencyKey } }
    );
  }

  pay(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[], idempotencyKey: string): Observable<CheckoutPayment> {
    return this.http.post<CheckoutPayment>(
      `${environment.apiBaseUrl}/api/v1/checkout/payments`,
      placePayload(customer, lines),
      { headers: { 'Idempotency-Key': idempotencyKey } }
    );
  }

  startPayment(orderId: string, checkoutAccessToken: string): Observable<CheckoutSession> {
    return this.http.post<CheckoutSession>(
      `${environment.apiBaseUrl}/api/v1/checkout/payment-sessions`,
      paymentSessionPayload(orderId),
      { headers: { 'Checkout-Access-Token': checkoutAccessToken } }
    );
  }

  outcome(orderId: string, checkoutAccessToken: string): Observable<CheckoutOutcome> {
    return this.http.post<CheckoutOutcome>(
      `${environment.apiBaseUrl}/api/v1/checkout/outcome`,
      checkoutOutcomePayload(orderId),
      { headers: { 'Checkout-Access-Token': checkoutAccessToken } }
    );
  }
}
