import { expect, test, type Locator, type Page } from '@playwright/test';
import { admin, categoryLabel, fixture, login, openNewProduct, password, reference, viewer } from './support';

test('el administrador entra en el panel y ve Usuarios', async ({ page }) => {
  await login(page, admin);
  await expect(page.getByRole('heading', { name: 'Panel de catálogo' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Panel' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Usuarios' })).toBeVisible();
});

test('un login incorrecto muestra el mensaje de acceso', async ({ page }) => {
  await page.goto('/admin/login');
  await page.getByLabel('Usuario').fill(admin);
  await page.getByLabel('Contraseña').fill('clave-que-no-existe');
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText('Usuario o contraseña incorrectos.')).toBeVisible();
  await expect(page).toHaveURL(/\/admin\/login/);
});

test('crea un producto estándar y aparece en el listado', async ({ page }) => {
  const ref = reference('E2E-STD');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await expect(dialog.getByLabel('Tipo de producto')).toHaveValue('Standard');
  await expect(dialog.getByLabel('Stock inicial')).toBeVisible();
  await fillProduct(dialog, ref);
  await dialog.getByLabel('Stock inicial').fill('4');
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expectCreated(page, ref);
  const row = page.getByRole('row', { name: ref });
  await expect(row.getByRole('cell', { name: 'Estándar' })).toBeVisible();
  await expect(row.getByRole('cell', { name: '4', exact: true })).toBeVisible();
});

test('crea un vino y muestra sus datos antes de guardarlo', async ({ page }) => {
  const ref = reference('E2E-WINE');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await dialog.getByLabel('Tipo de producto').selectOption({ label: 'Vino' });
  await expect(dialog.getByRole('heading', { name: 'Datos del vino' })).toBeVisible();
  await expect(dialog.getByLabel('Stock inicial')).toBeVisible();
  await fillProduct(dialog, ref);
  await dialog.getByLabel('Añada').fill('2024');
  await dialog.getByLabel('Uva').fill('Rufete');
  await dialog.getByLabel('Graduación alcohólica (%)').fill('13,50');
  await dialog.getByLabel('Stock inicial').fill('6');
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expectCreated(page, ref);
  const row = page.getByRole('row', { name: ref });
  await expect(row.getByRole('cell', { name: 'Vino' })).toBeVisible();
  await expect(row.getByRole('cell', { name: '6', exact: true })).toBeVisible();
});

test('crea un pack con componentes del propio test', async ({ page }) => {
  const first = reference('E2E-CMP');
  const second = reference('E2E-CMP');
  const pack = reference('E2E-PACK');
  await login(page, admin);
  await createStandard(page, first, '1');
  await createStandard(page, second, '1');

  const dialog = await openNewProduct(page);
  await dialog.getByLabel('Tipo de producto').selectOption({ label: 'Pack' });
  await expect(dialog.getByText('Stock: Derivado')).toBeVisible();
  await expect(dialog.getByLabel('Stock inicial')).toBeHidden();
  await fillProduct(dialog, pack);
  await addComponent(dialog, first, '2');
  await addComponent(dialog, second, '3');
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expectCreated(page, pack);
  await expect(page.getByRole('row', { name: pack }).getByRole('cell', { name: 'Derivado' })).toBeVisible();
});

test('un precio inválido mantiene el modal abierto', async ({ page }) => {
  const ref = reference('E2E-PRICE');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await fillProduct(dialog, ref, '-1');
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expect(dialog.getByText('El precio indicado no es válido.')).toBeVisible();
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('row', { name: ref })).toHaveCount(0);
});

test('un pack vacío no se crea', async ({ page }) => {
  const ref = reference('E2E-EMPTY');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await dialog.getByLabel('Tipo de producto').selectOption({ label: 'Pack' });
  await fillProduct(dialog, ref);
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expect(dialog.getByText('Un pack debe incluir al menos un componente.')).toBeVisible();
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('row', { name: ref })).toHaveCount(0);
});

test('edita nombre, descripción y stock de un producto del test', async ({ page }) => {
  const ref = reference('E2E-EDIT');
  const renamed = `${ref}-EDIT`;
  await login(page, admin);
  await createStandard(page, ref, '8');

  await page.getByRole('row', { name: ref }).getByRole('button', { name: 'Editar' }).click();
  const dialog = page.getByRole('dialog', { name: ref });
  await expect(dialog.getByLabel('Nombre')).toBeVisible();
  await dialog.getByLabel('Nombre').fill(renamed);
  await dialog.getByLabel('Descripción').fill('Descripción E2E actualizada');
  await dialog.getByLabel('Stock actual').fill('5');
  await dialog.getByRole('button', { name: 'Guardar' }).click();

  await expect(page.getByText(`Producto «${renamed}» actualizado correctamente.`)).toBeVisible();
  const row = page.getByRole('row', { name: renamed });
  await expect(row.getByRole('cell', { name: renamed })).toBeVisible();
  await expect(row.getByRole('cell', { name: '5', exact: true })).toBeVisible();

  await row.getByRole('button', { name: 'Editar' }).click();
  await expect(page.getByRole('dialog', { name: renamed }).getByLabel('Descripción')).toHaveValue('Descripción E2E actualizada');
});

test('crea un producto con una imagen válida y muestra la miniatura', async ({ page }) => {
  const ref = reference('E2E-IMG');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await fillProduct(dialog, ref);
  await dialog.locator('#create-image-file').setInputFiles(fixture('valid-800.png'));
  await expect(dialog.getByRole('img', { name: 'Imagen seleccionada' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expectCreated(page, ref);
  const row = page.getByRole('row', { name: ref });
  await expect(row.locator('img.thumb')).toHaveAttribute('src', /\/media\//);
  await expect(row.getByRole('button', { name: 'Cambiar imagen' })).toBeVisible();
});

test('una imagen demasiado pequeña no crea el producto', async ({ page }) => {
  const ref = reference('E2E-BADIMG');
  await login(page, admin);
  const dialog = await openNewProduct(page);
  await fillProduct(dialog, ref);
  await dialog.locator('#create-image-file').setInputFiles(fixture('tiny-100.png'));
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expect(dialog.getByText('La imagen debe medir al menos 800×800 px.')).toBeVisible();
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('row', { name: ref })).toHaveCount(0);
});

test('el administrador modifica un usuario temporal', async ({ page }) => {
  const username = `e2e-tmp-${Date.now().toString(36)}`;
  await login(page, admin);
  await page.getByRole('link', { name: 'Usuarios' }).click();
  await expect(page.getByRole('cell', { name: 'Acciones', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Guardar' }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cambiar' }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Desactivar' }).first()).toBeVisible();

  await page.getByLabel('Nombre de usuario').fill(username);
  await page.getByLabel('Nombre visible').fill('Temporal E2E');
  await page.getByLabel('Contraseña').fill(password);
  await page.getByLabel('Rol').selectOption('Viewer');
  await page.getByRole('button', { name: 'Crear' }).click();
  await expect(page.getByText('Usuario creado correctamente.')).toBeVisible();

  const row = page.getByRole('row', { name: username });
  await expect(row.getByRole('button', { name: 'Desactivar', exact: true })).toBeVisible();
  await row.getByRole('combobox').selectOption('Manager');
  await row.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Usuario actualizado correctamente.')).toBeVisible();
  await expect(page.getByRole('row', { name: username }).getByRole('combobox')).toHaveValue('Manager');
});

test('un Viewer consulta el panel y no puede modificarlo', async ({ page }) => {
  await login(page, viewer);
  await expect(page.getByRole('heading', { name: 'Panel de catálogo' })).toBeVisible();
  await expect(page.getByText('Tu rol permite consultar el catálogo, no modificarlo.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Usuarios' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: '+ Nuevo producto' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Importar productos' })).toHaveCount(0);
});

test('una ruta de administración inexistente responde 404', async ({ page }) => {
  const response = await page.goto('/admin/ruta-inexistente');
  expect(response?.status()).toBe(404);
  await expect(page.getByRole('heading', { name: 'Página no encontrada' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Volver al panel' })).toBeVisible();
});

test('una ruta de API inexistente responde ProblemDetails', async ({ request }) => {
  const response = await request.get('/api/v1/no-existe');
  expect(response.status()).toBe(404);
  expect(response.headers()['content-type'] ?? '').toContain('application/problem+json');
  expect(response.headers()['content-type'] ?? '').not.toContain('text/html');
  const body = await response.json();
  expect(body.status).toBe(404);
  expect(body.title).toBe('No se ha encontrado el recurso.');
  expect(JSON.stringify(body)).not.toContain('<html');
});

async function fillProduct(dialog: Locator, ref: string, price = '18,90') {
  await dialog.getByLabel('Referencia').fill(ref);
  await dialog.getByLabel('Categoría').selectOption({ label: categoryLabel });
  await dialog.getByLabel('Nombre').fill(ref);
  await dialog.getByLabel('Precio (€)').fill(price);
  await dialog.getByLabel('IVA (%)').fill('21');
}

async function createStandard(page: Page, ref: string, stock: string) {
  const dialog = await openNewProduct(page);
  await fillProduct(dialog, ref);
  await dialog.getByLabel('Stock inicial').fill(stock);
  await dialog.getByRole('button', { name: 'Crear producto' }).click();
  await expectCreated(page, ref);
}

async function addComponent(dialog: Locator, ref: string, quantity: string) {
  await dialog.getByRole('button', { name: '+ Añadir producto' }).click();
  const components = dialog.locator('#create-components');
  const select = components.getByRole('combobox').last();
  await expect(select).toBeVisible();
  await select.selectOption({ label: `${ref} — ${ref}` });
  await components.getByRole('spinbutton').last().fill(quantity);
}

async function expectCreated(page: Page, ref: string) {
  await expect(page.getByText(`Producto «${ref}» creado correctamente.`)).toBeVisible();
  await expect(page.getByRole('row', { name: ref })).toBeVisible();
}
