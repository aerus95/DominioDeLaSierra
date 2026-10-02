import { Directive, ElementRef, HostListener, inject, output } from '@angular/core';

/** Shared dismissal for custom menus; native selects retain browser behaviour. */
@Directive({ selector: '[dsDismissOutside]', standalone: true })
export class DismissOutsideDirective {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly dsDismissOutside = output<void>();

  @HostListener('document:click', ['$event']) onClick(event: MouseEvent): void {
    if (!event.composedPath().includes(this.element.nativeElement)) this.dsDismissOutside.emit();
  }
  @HostListener('document:focusin', ['$event']) onFocus(event: FocusEvent): void {
    if (!this.element.nativeElement.contains(event.target as Node)) this.dsDismissOutside.emit();
  }
  @HostListener('document:keydown.escape') onEscape(): void { this.dsDismissOutside.emit(); }
}
