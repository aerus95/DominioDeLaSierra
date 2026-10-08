import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { categoryGrapeLabel, showsWineSpec, wineAlcohol, wineGrape } from './wine-display.ts';

describe('variedad y graduación', () => {
  it('formatea el grado en español y conserva el cero', () => {
    assert.equal(wineAlcohol('Wine', 14), '14% vol.');
    assert.equal(wineAlcohol('Wine', 13.5), '13,5% vol.');
    assert.equal(wineAlcohol('Wine', 13.25), '13,25% vol.');
    assert.equal(wineAlcohol('Wine', 0), '0% vol.');
  });

  it('omite grado nulo y no lo aplica fuera de los vinos', () => {
    assert.equal(wineAlcohol('Wine', null), '');
    assert.equal(wineAlcohol('Wine', undefined), '');
    assert.equal(wineAlcohol('Standard', 14), '');
    assert.equal(wineAlcohol('Pack', 0), '');
    assert.equal(showsWineSpec(wineAlcohol('Wine', null)), false);
  });

  it('recorta la variedad y no muestra una fila vacía', () => {
    assert.equal(wineGrape('Wine', '  Rufete  '), 'Rufete');
    assert.equal(wineGrape('Wine', '   '), '');
    assert.equal(wineGrape('Wine', null), '');
    assert.equal(wineGrape('Standard', 'Rufete'), '');
    assert.equal(showsWineSpec(wineGrape('Wine', '   ')), false);
    assert.equal(showsWineSpec(wineGrape('Wine', 'Rufete')), true);
  });

  it('separa categoría y variedad solo cuando hay variedad', () => {
    assert.equal(categoryGrapeLabel('Tinto', 'Rufete'), 'Tinto · Rufete');
    assert.equal(categoryGrapeLabel('Tinto', ''), 'Tinto');
  });
});
