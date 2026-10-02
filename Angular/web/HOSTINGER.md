# Despliegue en Hostinger

Este proyecto se puede publicar desde el asistente **Web Apps / Node.js Apps** de
Hostinger usando el ZIP de código fuente preparado en la carpeta `release`.

## Ajustes de compilación

- Framework: Angular (detección automática)
- Node.js: 22.x
- Instalación: `npm ci`
- Compilación para revisión con datos mock: `npm run build:hostinger`
- Directorio de salida: `dist`
- Variables de entorno: ninguna en esta fase

Este release es para revisión: utiliza datos de demostración y no conecta con
la API local. No procesa pagos ni envía solicitudes de grupos automáticamente.

El archivo `package.json` debe quedar en la raíz del ZIP. No se deben incluir
`node_modules`, `dist`, `.git` ni otros paquetes de despliegue.

## Alternativa de alojamiento estático

El ZIP cuyo nombre contiene `Hostinger-v8` es la compilación estática. Se usa
desde el Administrador de archivos, extrayendo su contenido directamente en
`public_html`; no debe subirse al asistente de aplicaciones Node.js.
