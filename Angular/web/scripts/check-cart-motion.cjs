/* Unit doubles only, not a browser or a substitute for visual QA. */
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { runInNewContext } = require('node:vm');
const assert = require('node:assert/strict');
const ts = require('typescript');
const signal = initial => { let value=initial; const get=()=>value; get.set=next=>{value=next;}; return get; };
function load(file, modules, context) {
  const source=readFileSync(resolve(__dirname,'..',file),'utf8');
  const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,experimentalDecorators:true}}).outputText;
  const exports={};
  runInNewContext(js,{exports,require:name=>modules[name],setTimeout,clearTimeout,...context});
  return exports;
}
async function main() {
  let reduced=false, brokenImage=false, throwAnimation=false;
  const flyers=new Set(), flights=[];
  class AnimationDouble {
    constructor() { this.finished=new Promise((resolve,reject)=>{this.resolve=resolve;this.reject=reject;}); }
    cancel() { this.reject(Error('cancelled')); }
  }
  class ElementDouble {
    constructor() { this.isConnected=true; this.style={}; this.classes=new Set(); this.classList={add:name=>this.classes.add(name),remove:name=>this.classes.delete(name)}; this.rect={left:20,top:10,width:19,height:56}; }
    getBoundingClientRect() { return this.rect; }
    animate(frames,options) {
      if (throwAnimation) throw Error('unsupported animation');
      const animation=new AnimationDouble();
      if (this.className==='cart-flight-bottle') flights.push({animation,frames,options});
      else animation.resolve();
      return animation;
    }
  }
  class ImageDouble extends ElementDouble {
    constructor() { super(); this.naturalWidth=brokenImage?0:320; }
    setAttribute() {}
    decode() { return brokenImage?Promise.reject(Error('image unavailable')):Promise.resolve(); }
    remove() { flyers.delete(this); }
  }
  const slot=new ElementDouble(); slot.dataset={productId:'a'}; slot.style.getPropertyValue=()=>'-3deg';
  const count=new ElementDouble(), basket=new ElementDouble(), front=new ElementDouble();
  front.rect={left:0,top:20,width:76,height:51};
  basket.querySelectorAll=()=>[slot]; basket.querySelector=selector=>selector==='.harvest-front'?front:count;
  const source=new ElementDouble(); source.rect={left:200,top:250,width:100,height:300};
  const button=new ElementDouble(); button.closest=()=>({querySelector:()=>source});
  const context={window:{matchMedia:()=>({matches:reduced}),innerWidth:1440,innerHeight:900},document:{querySelector:()=>basket,body:{appendChild:element=>flyers.add(element)}},Element:ElementDouble,Image:ImageDouble,requestAnimationFrame:callback=>callback(),getComputedStyle:()=>({transform:'matrix(1,0,0,1,0,0)'})};
  const { animateBottleToCart }=load('src/app/core/cart-motion.ts',{'./product-visual':{bottleImage:()=>'/wine.webp',BASKET_LIP_CENTER:.35}},context);
  const event={currentTarget:button}, product={id:'a'};
  const flush=async()=>{ for(let i=0;i<8;i++) await Promise.resolve(); };
  let task=animateBottleToCart(event,product); await flush();
  assert.equal(flyers.size,1); assert.ok(button.classes.has('is-adding-to-cart'));
  assert.ok(flights.at(-1).options.duration>=700 && flights.at(-1).options.duration<=950);
  assert.ok(!flights.at(-1).frames.at(-1).clipPath.includes('NaN'));
  assert.ok(parseFloat(flights.at(-1).frames.at(-1).clipPath.split(' ')[2]) > 0);
  flights.at(-1).animation.resolve(); await task;
  assert.equal(flyers.size,0); assert.equal(button.classes.size,0);

  // Sparse bottles use their reduced resting size, in desktop and mobile.
  slot.rect={left:27,top:-8,width:64/3,height:64};
  task=animateBottleToCart(event,product); await flush();
  assert.ok(flights.at(-1).frames.at(-1).transform.includes('scale('+64/168+')'));
  const singleClip=parseFloat(flights.at(-1).frames.at(-1).clipPath.split(' ')[2]);
  assert.ok(singleClip > 20 && singleClip < 30);
  flights.at(-1).animation.resolve(); await task;
  context.window.innerWidth=390;
  slot.rect={left:23,top:-4,width:56/3,height:56};
  front.rect={left:0,top:21,width:65,height:44};
  task=animateBottleToCart(event,product); await flush();
  assert.ok(flights.at(-1).frames.at(-1).transform.includes('scale('+56/144+')'));
  assert.ok(!flights.at(-1).frames.at(-1).clipPath.includes('NaN'));
  flights.at(-1).animation.resolve(); await task;
  assert.equal(flyers.size,0);
  context.window.innerWidth=1440;

  // A second click cancels the first flight, without leaving DOM debris.
  const first=animateBottleToCart(event,product); await flush();
  const second=animateBottleToCart(event,product); await flush();
  assert.equal(flyers.size,1); flights.at(-1).animation.resolve();
  await Promise.all([first,second]); assert.equal(flyers.size,0); assert.equal(button.classes.size,0);

  reduced=true; const before=flights.length; await animateBottleToCart(event,product);
  assert.equal(flights.length,before); assert.equal(flyers.size,0);
  reduced=false; brokenImage=true; await animateBottleToCart(event,product);
  assert.equal(flyers.size,0); assert.equal(flights.length,before);
  brokenImage=false; throwAnimation=true; await animateBottleToCart(event,product);
  assert.equal(flyers.size,0); assert.equal(button.classes.size,0);

  const pending=[];
  const { CartFeedbackService }=load('src/app/core/cart-feedback.service.ts',{'@angular/core':{signal,Injectable:()=>target=>target},'./cart-motion':{animateBottleToCart:()=>new Promise(resolve=>pending.push(resolve))}},{});
  const feedback=new CartFeedbackService();
  const a=feedback.present(event,{id:'a'}), b=feedback.present(event,{id:'b'});
  assert.equal(feedback.pendingProduct().id,'b');
  pending[0](); await a; assert.equal(feedback.product(),null);
  pending[1](); await b; assert.equal(feedback.product().id,'b'); assert.equal(feedback.pendingProduct(),null);
  feedback.pause(); feedback.dismiss(); assert.equal(feedback.product(),null);
  console.log('OK: vuelo y limpieza, clics rápidos, movimiento reducido, imagen fallida, fallo de animación y confirmación del último producto.');
}
main().catch(error=>{console.error(error);process.exitCode=1;});
