import { AfterViewInit, Directive, ElementRef, HostListener, OnDestroy, inject } from '@angular/core';

/** Keyboard containment and focus restoration for non-native modal overlays. */
@Directive({ selector: '[dsModalFocus]', standalone: true })
export class ModalFocusDirective implements AfterViewInit, OnDestroy {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private previousFocus: HTMLElement | null = null;
  private previousOverflow = '';
  ngAfterViewInit(): void {
    this.previousFocus = document.activeElement as HTMLElement;
    this.previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    this.focusable()[0]?.focus();
  }
  @HostListener('keydown', ['$event']) onKey(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const controls = this.focusable();
    const first = controls[0], last = controls.at(-1);
    if (!first || !last) { event.preventDefault(); return; }
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
  ngOnDestroy(): void {
    document.body.style.overflow = this.previousOverflow;
    if (this.previousFocus?.isConnected) this.previousFocus.focus();
  }
  private focusable(): HTMLElement[] {
    return Array.from(this.element.nativeElement.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex="0"]')).filter(node => node.getClientRects().length > 0);
  }
}
