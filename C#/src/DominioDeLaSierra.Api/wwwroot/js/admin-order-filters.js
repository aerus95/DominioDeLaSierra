(function (root) {
  var names = ['number', 'customer', 'status', 'fulfillment', 'payment', 'from', 'to'];

  function fieldValue(form, name) {
    var element = form && form.elements.namedItem(name);
    return element ? String(element.value || '').trim() : '';
  }

  function query(form, page) {
    var params = new URLSearchParams();
    names.forEach(function (name) {
      var value = fieldValue(form, name);
      if (value) params.set(name, value);
    });
    if (page > 1) params.set('listPage', String(page));
    return params;
  }

  function applyFragment(html) {
    var live = root.AdminLiveFilters;
    var parsed = new DOMParser().parseFromString(html, 'text/html');
    var fragment = parsed.getElementById('order-fragment');
    var rows = document.getElementById('order-rows');
    if (!fragment || !rows) throw new Error('fragment');
    var block = document.getElementById('order-results');
    var section = rows.closest('section');
    var scrollX = root.scrollX;
    var scrollY = root.scrollY;
    var heightBefore = block ? block.offsetHeight : 0;
    var documentHeight = document.documentElement.scrollHeight;
    var sectionBottom = section ? section.getBoundingClientRect().bottom : 0;
    var active = document.activeElement;
    var focusVisible = false;
    if (active && section && section.contains(active) && !rows.contains(active) && active.getBoundingClientRect) {
      var rect = active.getBoundingClientRect();
      focusVisible = rect.bottom > 0 && rect.top < root.innerHeight;
    }
    var keepBelow = live.keepContentBelow(sectionBottom, root.innerHeight, focusVisible);
    if (block) block.style.minHeight = heightBefore + 'px';
    rows.replaceChildren(fragment.content.cloneNode(true));
    var heightAfter = rows.offsetHeight;
    var plan = live.planViewport({
      scrollY: scrollY,
      viewportHeight: root.innerHeight,
      sectionBottom: sectionBottom,
      keepBelow: keepBelow,
      heightBefore: heightBefore,
      heightAfter: heightAfter,
      documentHeight: documentHeight
    });
    if (block) {
      if (plan.minHeight > 0) {
        block.style.minHeight = plan.minHeight + 'px';
        block.dataset.naturalHeight = String(heightAfter);
      } else {
        block.style.minHeight = '';
        delete block.dataset.naturalHeight;
      }
    }
    if (root.scrollX !== scrollX || Math.abs(root.scrollY - plan.scrollY) > 0.5) {
      root.scrollTo(scrollX, plan.scrollY);
    }
    var count = document.getElementById('order-count');
    if (count) count.textContent = fragment.getAttribute('data-count') || '0';
    var reported = parseInt(fragment.getAttribute('data-page') || '1', 10);
    return reported > 0 ? reported : 1;
  }

  function start() {
    var live = root.AdminLiveFilters;
    var form = document.getElementById('order-filters');
    var rows = document.getElementById('order-rows');
    if (!live || !form || !rows) return;
    var refresh = new live.ListRefresh(function (url, options) {
      return fetch(url, Object.assign({ credentials: 'same-origin', headers: { 'Accept': 'text/html' } }, options));
    });
    var page = 1;
    var timer = 0;
    var initial = new URLSearchParams(root.location.search);
    var initialPage = parseInt(initial.get('listPage') || '1', 10);
    if (initialPage > 0) page = initialPage;

    function setHidden(id, message) {
      var element = document.getElementById(id);
      if (!element) return;
      if (message) element.textContent = message;
      element.hidden = !message;
    }

    function run() {
      var params = query(form, page);
      var next = root.location.pathname + (params.toString() ? '?' + params.toString() : '');
      root.history.replaceState(null, '', next);
      params.set('handler', 'Rows');
      setHidden('order-filter-status', 'Buscando…');
      setHidden('order-filter-error', '');
      var current = null;
      return refresh.run(root.location.pathname + '?' + params.toString()).then(function (result) {
        current = result;
        if (!result || result.aborted || result.stale || result.generation !== refresh.generation) return;
        if (result.failed || !result.response || !result.response.ok) throw new Error('search');
        return result.response.text().then(function (html) {
          if (result.generation !== refresh.generation) return;
          page = applyFragment(html);
        });
      }).catch(function () {
        if (current && current.generation === refresh.generation) {
          setHidden('order-filter-error', 'No se ha podido actualizar el listado.');
        }
      }).finally(function () {
        if (!current || current.generation === refresh.generation) setHidden('order-filter-status', '');
      });
    }

    function schedule(delay) {
      root.clearTimeout(timer);
      timer = root.setTimeout(run, delay);
    }

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      page = 1;
      schedule(0);
    });
    form.addEventListener('input', function (event) {
      var target = event.target;
      if (!target || names.indexOf(target.name) < 0) return;
      page = 1;
      schedule(target.tagName === 'INPUT' && target.type !== 'date' ? live.searchDebounceMs : 0);
    });
    form.addEventListener('change', function (event) {
      var target = event.target;
      if (!target || names.indexOf(target.name) < 0) return;
      page = 1;
      schedule(0);
    });
    var clear = document.getElementById('clear-order-filters');
    if (clear) {
      clear.addEventListener('click', function (event) {
        event.preventDefault();
        names.forEach(function (name) {
          var element = form.elements.namedItem(name);
          if (element) element.value = '';
        });
        page = 1;
        schedule(0);
      });
    }
    rows.addEventListener('click', function (event) {
      var link = event.target.closest('.order-page');
      if (!link) return;
      event.preventDefault();
      var requested = parseInt(link.getAttribute('data-page') || '1', 10);
      page = requested > 0 ? requested : 1;
      schedule(0);
    });
    root.addEventListener('scroll', function () {
      document.querySelectorAll('.live-results[data-natural-height]').forEach(function (block) {
        var natural = Number(block.dataset.naturalHeight) || 0;
        if (!live.canReleaseReservation({
          scrollY: root.scrollY,
          viewportHeight: root.innerHeight,
          visualHeight: block.offsetHeight,
          naturalHeight: natural,
          documentHeight: document.documentElement.scrollHeight
        })) return;
        block.style.minHeight = '';
        delete block.dataset.naturalHeight;
      });
    }, { passive: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})(globalThis);
