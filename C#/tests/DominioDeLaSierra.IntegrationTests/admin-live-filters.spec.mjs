import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { describe, it } from 'node:test';

const source = fs.readFileSync(new URL('../../src/DominioDeLaSierra.Api/wwwroot/js/admin-live-filters.js', import.meta.url), 'utf8');
const sandbox = {
  URLSearchParams,
  AbortController,
  Promise,
  Error,
  Object,
  String
};
sandbox.globalThis = sandbox;
vm.runInNewContext(source, sandbox);
const filters = sandbox.AdminLiveFilters;

describe('filtros dinámicos del panel', () => {
  it('aplica la búsqueda al escribir y los selects al momento', () => {
    assert.equal(filters.refreshDelay('input'), filters.searchDebounceMs);
    assert.equal(filters.refreshDelay('input'), 350);
    assert.equal(filters.refreshDelay('select'), 0);
    assert.equal(filters.refreshDelay('submit'), 0);
    assert.equal(filters.refreshDelay('clear'), 0);
  });

  it('conserva los filtros de cada listado por separado', () => {
    const current = {
      categoryQuery: 'tinto',
      categoryActive: 'true',
      categoryParent: 'none',
      productQuery: 'rufete',
      productCategory: 'abc',
      productKind: 'Pack',
      productActive: 'false'
    };
    const params = filters.catalogQuery(current);
    assert.equal(params.get('categoryQuery'), 'tinto');
    assert.equal(params.get('productKind'), 'Pack');
    assert.equal(params.get('productQuery'), 'rufete');

    const clearedProducts = filters.clearFields(current, 'product');
    assert.equal(clearedProducts.categoryQuery, 'tinto');
    assert.equal(clearedProducts.categoryParent, 'none');
    assert.equal(clearedProducts.productQuery, '');
    assert.equal(clearedProducts.productKind, '');

    const clearedCategories = filters.clearFields(current, 'category');
    assert.equal(clearedCategories.productQuery, 'rufete');
    assert.equal(clearedCategories.categoryQuery, '');
    assert.equal(filters.catalogQuery({ productQuery: '  ' }).toString(), '');
  });

  it('mantiene el scroll al encoger un listado visible y al actualizar el de arriba', () => {
    const visible = filters.planViewport({
      scrollY: 640,
      viewportHeight: 800,
      sectionBottom: 420,
      heightBefore: 900,
      heightAfter: 120,
      documentHeight: 2400
    });
    assert.equal(visible.scrollY, 640);
    assert.equal(visible.minHeight, 0);

    const above = filters.planViewport({
      scrollY: 1400,
      viewportHeight: 800,
      keepBelow: true,
      heightBefore: 900,
      heightAfter: 120,
      documentHeight: 2400
    });
    assert.equal(above.scrollY, 620);
    assert.equal(above.minHeight, 0);

    const clamped = filters.planViewport({
      scrollY: 1600,
      viewportHeight: 800,
      sectionBottom: 900,
      heightBefore: 1800,
      heightAfter: 80,
      documentHeight: 2600
    });
    assert.equal(clamped.scrollY, 1600);
    assert.equal(clamped.minHeight, 1600);

    const atBottom = filters.planViewport({
      scrollY: 1028,
      viewportHeight: 1080,
      sectionBottom: 1009,
      heightBefore: 1218,
      heightAfter: 79,
      documentHeight: 2108
    });
    assert.equal(atBottom.scrollY, 1028);
    assert.equal(atBottom.minHeight, 1218);

    const emptyAbove = filters.planViewport({
      scrollY: 400,
      viewportHeight: 700,
      keepBelow: true,
      heightBefore: 500,
      heightAfter: 40,
      documentHeight: 1200
    });
    assert.equal(emptyAbove.scrollY, 0);
    assert.equal(emptyAbove.minHeight, 0);
    assert.equal(filters.keepContentBelow(-40, 800, false), true);
    assert.equal(filters.keepContentBelow(7, 1080, false), true);
    assert.equal(filters.keepContentBelow(7, 1080, true), false);
    assert.equal(filters.keepContentBelow(700, 1080, false), false);

    assert.equal(filters.canReleaseReservation({
      scrollY: 200,
      viewportHeight: 800,
      visualHeight: 400,
      naturalHeight: 80,
      documentHeight: 2000
    }), true);
    assert.equal(filters.canReleaseReservation({
      scrollY: 1500,
      viewportHeight: 800,
      visualHeight: 1100,
      naturalHeight: 80,
      documentHeight: 2300
    }), false);
  });

  it('no deja que una respuesta antigua sustituya a la más reciente', async () => {
    let releaseFirst;
    const firstGate = new Promise((resolve) => {
      releaseFirst = resolve;
    });
    let calls = 0;
    const refresher = new filters.ListRefresh(async (_url, options) => {
      calls += 1;
      if (calls === 1) {
        await firstGate;
        if (options.signal.aborted) {
          const error = new Error('aborted');
          error.name = 'AbortError';
          throw error;
        }
        return { body: 'antigua' };
      }
      return { body: 'reciente' };
    });

    const older = refresher.run('/antigua');
    const newer = refresher.run('/reciente');
    releaseFirst();
    const [first, second] = await Promise.all([older, newer]);

    assert.equal(first.aborted, true);
    assert.equal(first.stale, true);
    assert.equal(second.stale, false);
    assert.equal(second.response.body, 'reciente');
  });
});
