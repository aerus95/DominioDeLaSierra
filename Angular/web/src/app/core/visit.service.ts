import { Injectable } from '@angular/core';
import { VisitAvailability, VisitBooking } from './models';

@Injectable({ providedIn: 'root' })
export class VisitService {
  readonly availability = this.createAvailability(120);

  book(booking: VisitBooking): VisitBooking {
    return booking;
  }

  private createAvailability(days: number): VisitAvailability[] {
    const firstDay = new Date();
    firstDay.setHours(12, 0, 0, 0);
    firstDay.setDate(firstDay.getDate() + 1);

    return Array.from({ length: days }, (_, index) => {
      const date = new Date(firstDay);
      date.setDate(firstDay.getDate() + index);
      const weekday = date.getDay();
      const slots = weekday === 0 ? ['11:00', '13:00'] : weekday === 6
        ? ['11:00', '13:00', '17:30']
        : ['10:30', '12:30', '17:30'];
      return { date: this.toIsoDate(date), slots };
    });
  }

  private toIsoDate(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
