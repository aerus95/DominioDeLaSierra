(function (root) {
  var filterNames = [
    'categoryQuery',
    'categoryActive',
    'categoryParent',
    'productQuery',
    'productCategory',
    'productKind',
    'productActive'
  ];
  var categoryNames = ['categoryQuery', 'categoryActive', 'categoryParent'];
  var productNames = ['productQuery', 'productCategory', 'productKind', 'productActive'];

  function catalogQuery(fields) {
    var params = new URLSearchParams();
    filterNames.forEach(function (name) {
      var value = fields && fields[name] ? String(fields[name]).trim() : '';
      if (value) params.set(name, value);
    });
    return params;
  }

  function clearFields(fields, side) {
    var next = Object.assign({}, fields || {});
    (side === 'category' ? categoryNames : productNames).forEach(function (name) {
      next[name] = '';
    });
    return next;
  }

  function refreshDelay(kind) {
    return kind === 'input' ? 350 : 0;
  }

  // Keeps the viewport still when a list changes height.
  // If the user is looking below that list, scroll moves with the height delta.
  // If a shrink would pull the page out from under the current scroll, the list
  // keeps a temporary min-height until the user scrolls back up.
  function planViewport(state) {
    var before = Number(state.heightBefore) || 0;
    var after = Number(state.heightAfter) || 0;
    var delta = after - before;
    var targetY = Number(state.scrollY) || 0;
    if (state.keepBelow) targetY += delta;
    if (targetY < 0) targetY = 0;

    var documentAfter = (Number(state.documentHeight) || 0) + delta;
    var maxY = documentAfter - (Number(state.viewportHeight) || 0);
    var minHeight = 0;
    if (targetY > maxY + 0.5) minHeight = Math.ceil(after + (targetY - maxY));
    return { scrollY: targetY, minHeight: minHeight };
  }

  function keepContentBelow(sectionBottom, viewportHeight, focusVisibleInSection) {
    if (focusVisibleInSection) return false;
    var limit = Math.min((Number(viewportHeight) || 0) * 0.35, 180);
    return Number(sectionBottom) < limit;
  }

  function canReleaseReservation(state) {
    var extra = Math.max(0, (Number(state.visualHeight) || 0) - (Number(state.naturalHeight) || 0));
    if (extra <= 1) return true;
    var naturalMax = Math.max(0, (Number(state.documentHeight) || 0) - extra - (Number(state.viewportHeight) || 0));
    return (Number(state.scrollY) || 0) <= naturalMax + 1;
  }

  function ListRefresh(fetchImpl) {
    this.fetchImpl = fetchImpl;
    this.generation = 0;
    this.controller = null;
  }

  ListRefresh.prototype.run = function (url) {
    var self = this;
    var generation = ++self.generation;
    if (self.controller) self.controller.abort();
    var controller = new AbortController();
    self.controller = controller;
    return Promise.resolve()
      .then(function () {
        return self.fetchImpl(url, { signal: controller.signal });
      })
      .then(function (response) {
        if (generation !== self.generation) return { stale: true, generation: generation };
        return { stale: false, generation: generation, response: response };
      })
      .catch(function (error) {
        if (error && error.name === 'AbortError') return { aborted: true, stale: true, generation: generation };
        if (generation !== self.generation) return { stale: true, generation: generation };
        return { failed: true, generation: generation };
      });
  };

  root.AdminLiveFilters = {
    searchDebounceMs: 350,
    catalogQuery: catalogQuery,
    clearFields: clearFields,
    refreshDelay: refreshDelay,
    planViewport: planViewport,
    keepContentBelow: keepContentBelow,
    canReleaseReservation: canReleaseReservation,
    ListRefresh: ListRefresh
  };
})(globalThis);
