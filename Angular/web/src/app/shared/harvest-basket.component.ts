import { Component, computed, inject, input } from '@angular/core';
import { CartFeedbackService } from '../core/cart-feedback.service';
import { Product } from '../core/models';
import { BASKET_FRONT_CLIP, basketLayout } from '../core/product-visual';

@Component({ selector: 'ds-harvest-basket', standalone: true, template: `
  <span class="harvest-basket" [class.is-sparse]="products().length > 0 && products().length <= 2" [class.is-full]="count() >= 6" aria-hidden="true">
    <img class="harvest-rear" src="/assets/brand/wicker-basket-cart.webp" alt="" width="480" height="320">
    <span class="harvest-bottles">
      @for (product of products(); track $index; let index = $index) {
        <span class="harvest-slot" [class.is-receiving]="receivingIndex() === index" [attr.data-product-id]="product.id" [style.left.%]="slots()[index].x" [style.height.px]="slots()[index].height" [style.--tilt]="slots()[index].tilt + 'deg'">
          <img [src]="product.image" alt="" width="320" height="960">
        </span>
      }
    </span>
    <img class="harvest-front" [style.clip-path]="frontClip" src="/assets/brand/wicker-basket-cart.webp" alt="" width="480" height="320">
    <span class="harvest-count">{{ count() }}</span>
  </span>
`, styles: [`
  :host { display:block; flex:0 0 auto; }
  .harvest-basket { position:relative; display:block; width:76px; height:64px; isolation:isolate; }
  .harvest-rear,.harvest-front { position:absolute; bottom:-1px; left:0; width:76px; height:51px; object-fit:contain; pointer-events:none; transition:bottom .32s cubic-bezier(.22,1,.36,1); }
  .is-sparse .harvest-rear,.is-sparse .harvest-front { bottom:-7px; }
  .harvest-rear { z-index:0; filter:drop-shadow(0 2px 2px #39251718); }
  .harvest-bottles { position:absolute; inset:0; z-index:2; pointer-events:none; }
  .harvest-slot { position:absolute; bottom:8px; aspect-ratio:1/3; transform:translateX(-50%) rotate(var(--tilt)); transform-origin:50% 80%; transition:left .32s cubic-bezier(.22,1,.36,1),height .32s cubic-bezier(.22,1,.36,1),bottom .32s cubic-bezier(.22,1,.36,1); }
  .harvest-slot img { display:block; width:100%; height:100%; object-fit:contain; filter:drop-shadow(1px 1px 1px #26160f33); }
  .harvest-slot.is-receiving { opacity:0; }
  .harvest-front { z-index:3; }
  .harvest-count { position:absolute; z-index:4; right:-3px; top:0; min-width:20px; height:20px; padding:0 4px; border:2px solid #faf8f4; border-radius:20px; background:#512638; color:#fff; display:grid; place-items:center; font:600 10px/1 'DM Sans',sans-serif; box-sizing:border-box; }
  @media(max-width:600px) { .harvest-basket { width:65px; height:58px; } .harvest-rear,.harvest-front { width:65px; height:44px; } .harvest-slot { bottom:6px; max-height:53px; } .is-sparse .harvest-slot { max-height:56px; } .harvest-count { right:-1px; font-size:9px; } }
  @media(prefers-reduced-motion:reduce) { .harvest-slot,.harvest-rear,.harvest-front { transition:none; } }
`] })
export class HarvestBasketComponent {
  private readonly feedback = inject(CartFeedbackService);
  readonly products = input.required<Product[]>();
  readonly count = input.required<number>();
  readonly frontClip = BASKET_FRONT_CLIP;
  readonly slots = computed(() => basketLayout(this.products().length));
  readonly receivingIndex = computed(() => this.products().map(product => product.id).lastIndexOf(this.feedback.pendingProduct()?.id ?? ''));
}
