# Arranque del entorno de desarrollo

Este arranque es solo para el portátil. Deja PostgreSQL, Mailpit, la API, la tienda, el relay de PayPal y un túnel temporal de Cloudflare listos para probar Sandbox. No crea pedidos, no cobra, no aplica migraciones y no habla con el VPS.

## Requisitos previos

- Windows con PowerShell 5.1 o posterior.
- SDK de .NET 10.
- Node.js. El proyecto declara `22.x`. Si hay otra versión, el script avisa y sigue; no cambia la instalación.
- npm.
- Docker Desktop en ejecución.
- `cloudflared` en el PATH o en `C:\Program Files (x86)\cloudflared\`.
- El archivo `.env` en la raíz del repositorio, fuera de Git, con `POSTGRES_PASSWORD`.
- Una base local `dominio_sierra` ya creada. Este arranque no aplica migraciones ni importa pedidos.
- Credenciales de una aplicación de **PayPal Sandbox**. No sirven las de Live.

El script no instala nada de esa lista.

## Comando único

Desde cualquier directorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1
```

Si la terminal no está en la raíz del repositorio, usa la ruta del script. El script localiza la raíz por su propia ubicación, sin depender del nombre de usuario.

La segunda ejecución no abre otro proceso si ese servicio ya responde. Si algo falló, se puede volver a lanzar el mismo comando: lo que ya está sano se deja como está.

## Credenciales de PayPal

Cuando la API no está en marcha, el script pregunta en la terminal:

1. Client ID de Sandbox, visible mientras se escribe.
2. Client Secret, con entrada oculta. No queda en el historial de PowerShell.
3. Webhook ID. Si se pulsa Enter, usa `9UC40709PV7468705`. Se puede escribir otro.

Esas credenciales no se guardan en `.env`, JSON, logs ni en archivos de Git. Tampoco van en argumentos de la línea de comandos. Viajan por la entrada estándar del proceso interno que arranca solo la API, y quedan en el entorno de ese proceso de API mientras vive.

Angular, el relay y `cloudflared` arrancan sin esas variables, aunque la terminal del usuario las tuviera definidas.

No es almacenamiento cifrado. Es el entorno del proceso de la API, que lo necesita para llamar a Sandbox. Al detener la API, ese entorno desaparece con el proceso.

No pegues el Client Secret en el chat ni lo asignes con `$env:PayPal__ClientSecret = ...`: esa asignación sí entra en el historial.

El resumen no muestra el Client ID completo. Como mucho enseña los cuatro últimos caracteres. El Webhook ID aparece como `configurado` o `pendiente`.

La API solo recibe `PayPal__Environment=Sandbox`. Si el proceso ya tiene ese valor en `Live`, el script se niega a arrancar la API.

## Qué deja en marcha

| Pieza | Dirección | Notas |
| --- | --- | --- |
| PostgreSQL 17 | `127.0.0.1:5432` | Base `dominio_sierra`. No borra el volumen. |
| Mailpit | `http://127.0.0.1:8025` | El script apaga el envío de avisos. |
| API | `http://localhost:5141` | Development. Panel en `/admin/login`. |
| Tienda | `http://localhost:4200` | |
| Relay | `http://127.0.0.1:5088` | Solo reenvía dos POST: el webhook de PayPal y la notificación de Redsys. |
| Túnel | URL `https://….trycloudflare.com` | Apunta solo al relay. |

La API arranca con los barridos, la conciliación automática y la emisión de facturas desactivados. Stripe no se activa. Redsys tampoco, salvo el arranque `-WithRedsys` descrito más abajo. En el arranque habitual `Redsys__Enabled` queda en `false` y las claves de Redsys no se copian al proceso.

El checkout de Development sigue exigiendo un Administrator autenticado. El script no crea ese usuario ni cambia su contraseña.

## Cómo comprobar que responde

Al terminar, el resumen debe marcar PostgreSQL y Mailpit como `OK`. Después:

- `http://localhost:5141/swagger/index.html` abre la API.
- `http://localhost:5141/admin/login` abre el panel.
- `http://localhost:4200` abre la tienda.
- Un GET a `http://127.0.0.1:5088/api/v1/checkout/paypal/webhook` devuelve **405**.
- Un GET a `http://127.0.0.1:5088/api/v1/checkout/redsys/notification` también devuelve **405**.
- Un GET a la URL pública que imprime el script, terminada en `/api/v1/checkout/paypal/webhook`, también devuelve **405**.
- La misma URL pública terminada en `/api/v1/checkout/redsys/notification` devuelve **405**.
- `GET /admin/login` y `GET /api/v1/checkout/payments`, en local y por el túnel, devuelven **404**.

Ese 405 significa que la petición llegó al relay y que el filtro rechazó GET. No significa que PayPal haya firmado un webhook, ni que Redsys haya notificado un pago. El relay no publica el panel ni el resto de la API: cualquier otra ruta responde 404.

El script no registra la URL en PayPal Developer.

## Cómo actualizar el webhook en PayPal

Hazlo a mano, cada vez que el túnel sea nuevo. La URL `trycloudflare.com` cambia al crear otro túnel y no se reutiliza una URL de un túnel ya cerrado.

1. Copia la URL completa que imprime el script, incluida `/api/v1/checkout/paypal/webhook`.
2. Entra en PayPal Developer, en la aplicación Sandbox cuyo Webhook ID estás usando.
3. Sustituye la URL del webhook existente por esa URL.
4. No crees otro webhook si quieres conservar el mismo Webhook ID.

Si el túnel no entrega una URL válida, la API, la tienda y el relay siguen en marcha. El resumen lo indica. Una URL escrita en el log no cuenta hasta que cloudflared registre la conexión, el nombre resuelva y un GET público devuelva 405. No se reutiliza una URL de un túnel ya cerrado.

## Redsys TEST

El arranque habitual deja Redsys apagado. Para probar la pasarela junto con PayPal Sandbox:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -WithRedsys
```

Ese modo abre primero el relay y el túnel. Solo cuando la URL pública actual responde, pide las credenciales y arranca o reinicia la API. Así `Redsys__MerchantUrl` sale del túnel que acaba de registrarse, no de uno anterior:

`https://<tunel-actual>.trycloudflare.com/api/v1/checkout/redsys/notification`

En la terminal pide, por este orden:

1. Las credenciales de PayPal Sandbox, igual que el arranque habitual, si la API se va a crear o a reiniciar.
2. El código de comercio TEST, 9 dígitos, visible mientras se escribe.
3. El terminal TEST, visible.
4. La clave de firma TEST, con entrada oculta. No queda en el historial.

No se guardan en `.env`, JSON, logs, Git ni en la línea de comandos. Viajan por la entrada estándar del proceso interno que arranca solo la API. Angular, el relay y `cloudflared` no las reciben. Al detener la API desaparecen con el proceso.

Redsys solo se habilita si el entorno de la API es Development, `Redsys__Environment` es `Test`, la firma es `HMAC_SHA512_V2` y el comercio, el terminal, la clave y las tres URLs están completos. Un valor `Live` en el proceso detiene el arranque. El retorno correcto es `http://localhost:4200/checkout/exito` y el de cancelación `http://localhost:4200/checkout/cancelado`.

Si la API ya estaba en marcha, este modo la reinicia para incorporar esas variables. PostgreSQL, Mailpit, Angular, el relay y el túnel no se reinician por eso. Si el relay todavía no admite la notificación, se sustituye solo ese proceso antes de tocar la API.

Si más tarde `-Only Cloudflare` crea otra URL y la API sigue con la anterior, el resumen marca `MerchantUrl obsoleta` y no reinicia nada. La recuperación, que pide otra vez las credenciales y reinicia solo la API, es:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -Only Redsys
```

Ninguno de estos comandos crea un pedido ni envía una operación a Redsys. La clave no se pega en el chat.

Para reintentar solo el túnel, sin volver a pedir las credenciales y sin reiniciar API, Angular, relay ni PostgreSQL:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -Only Cloudflare
```

Si ese túnel sigue sin resolver, el comando reinicia únicamente cloudflared y espera la URL nueva. Un error DNS temporal, incluido `curl: (6)`, no detiene el resto del arranque.

## Logs

Están en `.dev\logs\`, carpeta ignorada por Git:

```powershell
Get-Content -Wait .\.dev\logs\api.out.log
Get-Content -Wait .\.dev\logs\api.err.log
Get-Content -Wait .\.dev\logs\angular.out.log
Get-Content -Wait .\.dev\logs\relay.err.log
Get-Content -Wait .\.dev\logs\cloudflared.err.log
```

Los procesos siguen vivos cuando el script termina. No hace falta dejar esa terminal abierta.

## Detener y reiniciar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\stop-dev.ps1
```

`stop-dev.ps1` solo detiene los procesos que `start-dev.ps1` registró en `.dev\state.json`, y solo si su línea de comandos sigue siendo la esperada. No usa `docker compose down`, no borra volúmenes y no toca pedidos ni stock.

PostgreSQL y Mailpit se paran únicamente si los había arrancado el propio script. Si ya estaban en marcha, se dejan así.

Para reiniciar, ejecuta otra vez `start-dev.ps1`. Pedirá de nuevo las credenciales si la API no sigue en marcha.

## Errores frecuentes

- **Docker Desktop cerrado.** El script se detiene antes de la API. Ábrelo y repite el comando.
- **Falta `.env` o `POSTGRES_PASSWORD`.** La API no puede leer la base. El script no inventa la contraseña.
- **El puerto 5141, 4200 o 5088 ya está ocupado** por un proceso ajeno. No se crea un duplicado. Detén ese proceso y vuelve a lanzar el script. Una API arrancada a mano no recibe las credenciales de esta ejecución.
- **La terminal no es interactiva** y no hay credenciales en el proceso. Ábrelo en la terminal de Cursor. No pegues el secreto en el chat.
- **Node no es 22.x.** Es un aviso. La tienda arranca con el Node instalado.
- **Falta `node_modules`.** El script pide permiso antes de `npm ci`.
- **El túnel no muestra una URL.** Revisa `.dev\logs\cloudflared.err.log`. El resto puede seguir funcionando. Reintenta con `start-dev.ps1 -Only Cloudflare`.
- **`curl: (6)` o el DNS del portátil dice que el nombre no existe.** El túnel puede estar bien y el resolutor local, no. El script consulta también 1.1.1.1 y 8.8.8.8, reintenta con esperas crecientes y solo entonces reinicia cloudflared. PayPal usa el DNS público.
- **La URL pública no devuelve 405.** No la des de alta en PayPal. Ejecuta `start-dev.ps1 -Only Cloudflare` para comprobar o pedir otra URL. No hace falta parar la API.
- **PayPal rechaza un pago de prueba.** Este arranque no lo diagnostica. El 405 solo demuestra que el túnel llega al relay.

## Diferencia con producción

Este script no es un despliegue. La tienda pública, el VPS y PayPal Live quedan fuera.

- Aquí la API escucha en localhost y el checkout de Development solo admite un Administrator.
- PayPal se fuerza a Sandbox. La aplicación rechaza un entorno Live.
- El túnel es temporal y hay que copiar su URL en el webhook Sandbox. Producción usa su propio nombre público, no `trycloudflare.com`.
- La base es `dominio_sierra` en el PostgreSQL local. No se migran datos ni se vacían tablas.
- Los avisos por correo, los barridos de reservas, la conciliación y las facturas quedan apagados en este arranque.

## Archivos

- `scripts/start-dev.ps1`: comando de arranque.
- `scripts/stop-dev.ps1`: detiene solo lo registrado por ese arranque.
- `scripts/dev-supervisor.ps1`: proceso interno. No hace falta ejecutarlo a mano.
- `.dev/`: estado, PIDs y logs. Está en `.gitignore` y no debe contener credenciales.
