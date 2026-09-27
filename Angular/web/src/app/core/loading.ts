declare global {
  interface Window { dsLoadingTimeout?: ReturnType<typeof setTimeout>; }
}

/** Boot overlay lives in index.html so it is visible before Angular downloads. */
export function finishWelcomeLoading(): void {
  clearTimeout(window.dsLoadingTimeout);
  const loader = document.getElementById('welcome-loader');
  const root = document.querySelector('ds-root');
  root?.removeAttribute('inert');
  root?.removeAttribute('aria-busy');
  if (!loader) return;
  loader.setAttribute('aria-hidden', 'true');
  loader.classList.add('leaving');
  const remove = () => loader.remove();
  loader.addEventListener('transitionend', remove, { once: true });
  // Fallback for reduced motion and background tabs without transition events.
  setTimeout(remove, 900);
}

export function failWelcomeLoading(): void {
  clearTimeout(window.dsLoadingTimeout);
  document.getElementById('welcome-loader')?.classList.add('failed');
  const caption = document.getElementById('welcome-caption');
  if (caption) caption.textContent = 'No hemos podido abrir la bodega. Vuelve a intentarlo.';
}
