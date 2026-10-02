/** Contact requests, not bookings or availability guarantees. */
export const GROUP_CONTACT_EMAIL = 'info@dominiodelasierra.com';
export const GROUP_MIN_PEOPLE = 7;

export interface GroupVisitContact {
  name: string;
  email: string;
  phone: string;
  approximatePeople: number | null;
  privacy: boolean;
}
export interface GroupVisitContext {
  experience: string;
  preferredDate?: string;
  preferredTime?: string;
}
export function validGroupPhone(phone: string): boolean {
  const digits = phone.replace(/\D/g, '');
  return /^\+?[\d\s().-]+$/.test(phone.trim()) && digits.length >= 7 && digits.length <= 15;
}
export function validGroupContact(value: GroupVisitContact): boolean {
  return value.name.trim().length > 0 && value.name.trim().length <= 120
    && value.email.trim().length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.email.trim())
    && value.phone.length <= 40 && validGroupPhone(value.phone)
    && Number.isSafeInteger(value.approximatePeople) && Number(value.approximatePeople) >= GROUP_MIN_PEOPLE
    && value.privacy === true;
}
/** Opens a draft in the visitor's email app; never claims server delivery. */
export function groupContactMailto(value: GroupVisitContact, context: GroupVisitContext): string {
  if (!validGroupContact(value)) return '';
  const body = [
    'Hola, me gustaría organizar una visita para un grupo grande.',
    '',
    'Nombre: ' + value.name.trim(),
    'Correo de contacto: ' + value.email.trim(),
    'Teléfono: ' + value.phone.trim(),
    'Personas aproximadas: ' + value.approximatePeople,
    'Experiencia de interés: ' + context.experience,
    ...(context.preferredDate ? ['Fecha orientativa: ' + context.preferredDate] : []),
    ...(context.preferredDate && context.preferredTime ? ['Hora orientativa: ' + context.preferredTime] : []),
    '',
    'Quedo pendiente de acordar la disponibilidad, el precio y los detalles con la bodega.',
    'Esta solicitud no confirma una reserva.'
  ].join('\r\n');
  return 'mailto:' + GROUP_CONTACT_EMAIL + '?subject=' + encodeURIComponent('Visita de grupo · Dominio de la Sierra') + '&body=' + encodeURIComponent(body);
}
