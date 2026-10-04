import { expect, type Page } from '@playwright/test';
import path from 'node:path';

export const password = 'Test-Only-12345';
export const admin = 'e2e-admin';
export const viewer = 'e2e-viewer';
export const categoryLabel = 'E2E Catalogo (e2e-catalogo)';

export function reference(prefix: string) {
  return `${prefix}-${Date.now().toString(36)}-${Math.floor(Math.random() * 1000)}`.toUpperCase();
}

export function fixture(name: string) {
  return path.join(__dirname, '..', 'fixtures', name);
}

export async function login(page: Page, username: string) {
  await page.goto('/admin/login');
  await page.getByLabel('Usuario').fill(username);
  await page.getByLabel('Contraseña').fill(password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/admin$/);
}

export async function openNewProduct(page: Page) {
  await page.getByRole('button', { name: '+ Nuevo producto' }).click();
  return page.getByRole('dialog', { name: 'Nuevo producto' });
}

export async function fillCommonProduct(page: Page, ref: string, price = '18,90') {
  const dialog = page.getByRole('dialog', { name: 'Nuevo producto' });
  await dialog.getByLabel('Referencia').fill(ref);
  await dialog.getByLabel('Categoría').selectOption({ label: categoryLabel });
  await dialog.getByLabel('Nombre').fill(ref);
  await dialog.getByLabel('Precio (€)').fill(price);
  await dialog.getByLabel('IVA (%)').fill('21');
  return dialog;
}
