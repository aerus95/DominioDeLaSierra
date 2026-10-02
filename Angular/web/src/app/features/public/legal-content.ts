export interface LegalPage { title: string; intro: string; sections: { title: string; text: string }[]; }

/** Review drafts: do not enable live sales until company and processor details are confirmed. */
export const legalPages: Record<string, LegalPage> = {
  'aviso-legal': {
    title: 'Aviso legal', intro: 'Información sobre esta web de Dominio de la Sierra.',
    sections: [
      { title: 'Titular y contacto', text: 'Dominio de la Sierra S.L. · NIF B37540762. Bodega en San Esteban de la Sierra, Salamanca. Contacto: info@dominiodelasierra.com. El domicilio social completo y los datos registrales están pendientes de validación por la titular antes de la publicación comercial.' },
      { title: 'Uso de la web', text: 'La tienda y las experiencias están dirigidas a personas mayores de 18 años. Utiliza la web de forma lícita y no introduzcas información falsa ni intentes acceder a datos de otras personas.' },
      { title: 'Contenidos y servicios externos', text: 'Los contenidos identifican a la bodega y sus productos. Algunas imágenes son composiciones ilustrativas, no fotografías de las instalaciones ni reproducciones exactas del etiquetado. Los mapas y enlaces externos pertenecen a sus respectivos proveedores.' },
      { title: 'Versión de revisión', text: 'Esta versión permite revisar el catálogo, los formularios y el proceso de compra. No procesa pagos, no confirma pedidos comerciales y no constituye una reserva efectiva. La información definitiva de precio, disponibilidad y transporte debe validarse en el servidor antes del pago.' }
    ]
  },
  privacidad: {
    title: 'Política de privacidad', intro: 'Queremos que sepas qué ocurre con tus datos en esta versión.',
    sections: [
      { title: 'Responsable', text: 'Dominio de la Sierra S.L., NIF B37540762. Para cuestiones de privacidad: info@dominiodelasierra.com. El domicilio social y la información completa del responsable deben confirmarse antes de activar los servicios.' },
      { title: 'Formularios de demostración', text: 'Los datos que escribes en contacto, envío y reservas permanecen en la memoria de esta aplicación durante su uso. En esta versión no se envían como solicitudes, pedidos ni mensajes a la bodega. Evita introducir datos sensibles durante las pruebas.' },
      { title: 'Servicios externos', text: 'Las fotografías externas y las tipografías pueden requerir conexiones a sus proveedores. El mapa interactivo de Google solo se carga cuando pulsas el botón específico. WhatsApp y Google Maps se abren como servicios externos y aplican sus propias políticas.' },
      { title: 'Antes de activar la tienda', text: 'Se completarán las finalidades y bases jurídicas de cada tratamiento, los destinatarios y encargados, las posibles transferencias internacionales y los plazos de conservación. La aceptación de esta política no equivale a consentir publicidad.' },
      { title: 'Tus derechos', text: 'Puedes solicitar acceso, rectificación, supresión, oposición, limitación y portabilidad cuando proceda, escribiendo al contacto de privacidad. También puedes presentar una reclamación ante la Agencia Española de Protección de Datos, www.aepd.es.' }
    ]
  },
  cookies: {
    title: 'Cookies y almacenamiento', intro: 'Una explicación de los recursos que utiliza esta versión.',
    sections: [
      { title: 'Preferencia de mayoría de edad', text: 'La aplicación guarda ds-age-confirmed en sessionStorage para recordar la confirmación de mayoría de edad durante la sesión del navegador. No es una cookie y puedes eliminarlo desde los ajustes del navegador.' },
        { title: 'Analítica y publicidad', text: 'El frontend no incorpora intencionadamente herramientas de analítica ni publicidad. El panel de preferencias permite aceptar, rechazar o configurar la conexión opcional con Google Maps.' },
      { title: 'Mapa y servicios de terceros', text: 'El mapa interactivo de Google Maps no se carga hasta que lo solicitas expresamente en su ventana. Ese servicio puede emplear cookies y otros identificadores. Los enlaces a WhatsApp y Google Maps llevan a páginas de terceros. El hosting y los recursos externos deben auditarse antes de publicar la versión comercial.' },
        { title: 'Cómo gestionar tus preferencias', text: 'Guardamos ds-privacy-preferences en localStorage: versión de la elección, autorización de Google Maps y fecha de guardado. La elección caduca tras 180 días y puedes cambiarla desde Configurar cookies en el pie de página. Rechazar descarga el mapa integrado, pero no borra cookies que Google ya hubiera guardado; puedes eliminarlas desde tu navegador. Si el almacenamiento está bloqueado, la elección solo se aplica mientras la aplicación permanece abierta.' }
    ]
  },
  'condiciones-de-compra': {
    title: 'Compras, envíos y devoluciones', intro: 'Información preparada para revisar antes de activar la venta online.',
    sections: [
      { title: 'Estado de la tienda', text: 'Esta versión no acepta compras ni cobra reservas. Precios, impuestos, descuentos, stock y transporte se confirmarán en el backend antes del pago. Los vinos contienen sulfitos y su venta se dirige exclusivamente a mayores de 18 años.' },
      { title: 'Envíos', text: 'La web anterior publica plazos orientativos de 24–48 horas para la Península y 48–72 para Baleares desde la expedición, en días laborables. Son referencias pendientes de confirmar con la bodega y el transportista para la nueva tienda; no se garantizan en esta demostración.' },
      { title: 'Gastos de transporte', text: 'El importe final aparecerá antes del pago según destino y condiciones vigentes. El umbral y los costes mostrados en la demo son estimaciones y deben validarse, incluidos impuestos y posibles exclusiones territoriales, antes de activar pedidos.' },
      { title: 'Pagos y contrarreembolso', text: 'La pasarela de pago todavía no está activa. No se ha confirmado una modalidad de contrarreembolso y esta versión no la ofrece. Los métodos disponibles y sus condiciones se mostrarán en el checkout definitivo.' },
      { title: 'Incidencias y devoluciones', text: 'Si un envío presenta daños, conserva el embalaje y las fotografías y contacta con la bodega indicando el pedido. Antes de activar la tienda se publicarán el procedimiento de desistimiento, sus plazos, los costes y las excepciones legales aplicables, sin limitar los derechos del consumidor.' },
      { title: 'Visitas', text: 'Seleccionar una fecha en la demo no genera una reserva ni bloquea plazas. Se deberán publicar las condiciones de cancelación y cambio de fecha antes de activar el pago de experiencias.' }
    ]
  }
};
