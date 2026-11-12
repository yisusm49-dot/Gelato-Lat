<div align="center">
   <img width="125" src="logo.png" alt="Logo">
</div>

<div align="center">
  <h1><b>Gelato-Lat</b></h1>
  <p><i>Gelato para LAT-ADD: el catálogo de LAT-ADD dentro de Jellyfin</i></p>
</div>

Fork de [lostb1t/Gelato](https://github.com/lostb1t/Gelato) afinado para el addon de Stremio
**LAT-ADD**. Se conecta directo al manifest de LAT-ADD, sin AIOStreams en el medio: las
películas y series de LAT-ADD aparecen en tu biblioteca de Jellyfin y se reproducen en
cualquier cliente de Jellyfin, como si fueran tuyas.

> [!IMPORTANT]
> Este fork **mantiene el GUID del plugin original** (para conservar tu configuración y tus
> títulos importados). Por eso un servidor no puede tener los dos repositorios a la vez:
> quita el repositorio del Gelato original antes de instalar desde este.

## Qué hace

- **Catálogo completo**: importa los catálogos de películas y series de LAT-ADD a tu
  biblioteca y los mantiene al día con una tarea programada.
- **Una versión por calidad**: cada calidad (1080p, 4K, …) es una versión del título, con
  nombres cortos y sin emoji.
- **Ruta ⚡ primero, con respaldo**: se reproduce por la ruta alterna (relay) y, si falla, cae
  sola al enlace directo del nodo.
- **Enlaces firmados sin cortes**: si un enlace venció, el plugin pide uno nuevo y reintenta,
  sin que tengas que refrescar nada.
- **Biblioteca estable**: tus versiones, progreso y vistos se conservan aunque los enlaces
  cambien en cada sincronización.
- **Importes livianos**: imágenes bajo demanda, personas opcionales y reintentos ante errores
  del servidor.
- Del original se conservan la búsqueda, los ajustes por usuario y los pedidos desde Seerr.

## Instalación

Requiere **Jellyfin 12.1 o mayor**.

1. En Jellyfin, *Panel → Plugins → Repositorios*: borra el repositorio de `lostb1t/Gelato`
   si lo tienes.
2. Añade el repositorio del fork:
   `https://raw.githubusercontent.com/yisusm49-dot/Gelato-Lat/gh-pages/repository.json`
3. Instala **Gelato** desde ese repositorio y reinicia Jellyfin.
4. En los ajustes del plugin, pega tu **URL de manifest de LAT-ADD** (la personal que te da
   LAT-ADD) en el campo de la URL del addon. El campo dice *AIOStreams URL*, pero acepta el
   manifest de LAT-ADD tal cual.
5. Activa solo los catálogos `lat-add_movies` y `lat-add_series` (los demás son subconjuntos)
   y marca **Try the next stream when one doesn't start**
   (necesario para el respaldo ⚡ → nodo).
6. En cada biblioteca de Jellyfin, pon **Gelato** primero en los descargadores de metadatos e
   imágenes.
7. Ejecuta la tarea **Import Catalogs**, o busca un título y dale play.

Los valores recomendados para cada opción de la tarjeta **LAT-ADD**, el esquema de versiones
y el detalle técnico de cada cambio están en **[FORK.md](FORK.md)**.

## Preguntas frecuentes

- **¿Cambió el catálogo y no lo veo?** En los ajustes del plugin, pestaña *Catalogs*, pulsa
  **Refresh**; no hace falta reiniciar.
- **¿Quiero empezar de cero?** Ejecuta la tarea **WARNING: purge all gelato items** en
  *Panel → Tareas programadas*. Borra los títulos y su progreso.
- **¿Perdí títulos con su progreso?** Ejecuta la tarea **Repair watch state**: reimporta lo
  que falta y le devuelve el progreso.
- **¿Problemas o ideas?** Abre un [issue en este repositorio](https://github.com/yisusm49-dot/Gelato-Lat/issues),
  no en el del original.

## Créditos

Gelato es obra de [lostb1t](https://github.com/lostb1t) y sus colaboradores; este fork solo
añade lo necesario para LAT-ADD. Si te sirve, apoya el proyecto original:
⭐ [dale una estrella](https://github.com/lostb1t/Gelato) o
☕ [invítale un café en Ko-fi](https://ko-fi.com/lostb1t).
