<div align="center">
   <img width="125" src="logo.png" alt="Logo">
</div>

<div align="center">
  <h1><b>Gelato-Lat</b></h1>
  <p><i>Fork de Gelato para el addon LAT-ADD</i></p>
</div>

> [!IMPORTANT]
> Este es el **fork LAT-ADD** de [Gelato](https://github.com/lostb1t/Gelato). Mantiene el GUID
> del original a propósito (para conservar la configuración y los ítems ya importados), así que
> un servidor **no debe cargar los dos repositorios de plugins a la vez**: quita primero el del
> original (ver [Instalación](#instalación)). Detalle completo, versionado y valores
> recomendados: **[FORK.md](FORK.md)**.

## Qué es

Gelato lleva addons de Stremio a Jellyfin: los catálogos que elijas se importan como una
biblioteca normal, y el stream se resuelve al dar play — **no se descarga nada**. Este fork está
afinado para **LAT-ADD**, el addon que sirve video directo por HTTP desde su red de nodos:

- El manifest de LAT-ADD se lee **directo** (sin AIOStreams en el medio).
- Cada calidad aparece **una sola vez**: las rutas gemelas del relay (⚡) se fusionan en la
  misma versión, con nombre limpio (`1080p · WEB-DL · Latino · 6.0 GB`).
- La ruta del **relay se prefiere** y la del nodo queda como respaldo automático.
- Las versiones **sobreviven a las sincronizaciones** (las URLs firmadas cambian, la versión no).

## Requisitos

- **Jellyfin 12.1 o mayor** (`targetAbi 12.1.0.0`, sigue al original). En un servidor con
  Jellyfin 12.0 el plugin no aparece instalable: actualizar el servidor (recomendado 12.2).
- Un addon LAT-ADD con su URL de manifest, y al menos un catálogo (`Movies` / `Series`).

## Instalación

1. **Quita el repositorio del Gelato original** en *Panel → Plugins → Repositorios*. Con ambos
   cargados y el mismo GUID, Jellyfin instalaría la versión más alta de cualquiera de los dos.
2. Añade el repositorio de este fork:

   ```
   https://raw.githubusercontent.com/yisusm49-dot/Gelato-Lat/gh-pages/repository.json
   ```

3. Instala/actualiza **Gelato** desde *Panel → Plugins → Catálogos* y reinicia si Jellyfin lo
   pide.

## Configuración con LAT-ADD

1. En los ajustes del plugin, pega la URL del manifest de tu addon:

   ```
   https://<host-de-tu-addon>/<token>/manifest.json
   ```

   El **token** viaja en el path y es tu clave de acceso: no lo compartas ni lo publiques.

2. Activa los catálogos **Movies** y **Series** (los de anime, kdrama y `recent_*` son
   subconjuntos de esos dos).
3. En la tarjeta **LAT-ADD** de los ajustes, revisa las opciones del fork. Valores recomendados
   para la biblioteca completa:

   | Opción | Valor |
   |---|---|
   | `PreferRelayRoute` | activado |
   | `ImportPeople` | desactivado (o `PeopleLimit` 10–15) |
   | `LazyImages` | activado |
   | `CatalogMaxItems` | 2000 |
   | `MetaConcurrency` | 4 |
   | `PlaybackFailover` | activado (relay → nodo) |

4. Corre la tarea **Import Catalogs** en *Panel → Tareas programadas*. La primera corrida es la
   larga (metas, temporadas, personas); las siguientes solo procesan novedades.

## Qué cambia respecto al original

| Área | Cambio |
|---|---|
| Manifest | `resources` se acepta en forma corta (string) u objeto; un string hereda `types`/`idPrefixes` del manifest raíz. |
| Identidad | La versión se identifica por el path del archivo (`/media/<id>/<archivo>`), no por la URL firmada. |
| Versiones | Una por calidad: gemelas ⚡ fusionadas, relay primero con el nodo de respaldo, clips `⛔` descartados, `bingeGroup` sin `-relay`. |
| Vencimiento | URL vencida (6 h) → re-sync de los streams del título (máx. 10 s) y reintento con firma fresca. |
| Import | `CatalogMaxItems` 2000, `LazyImages` on, `ImportPeople`/`PeopleLimit`, `MetaConcurrency`, reintentos con backoff ante 429/5xx. |
| Identificación | User-Agent `Gelato-LAT-ADD/<versión>` en el tráfico hacia el addon. |

El detalle técnico, el esquema de versiones del fork (`<base-upstream>.<1000+n>`) y el
mantenimiento están en **[FORK.md](FORK.md)**.

## Desarrollo y pruebas

```
dotnet build -c Release     # el plugin
dotnet test                 # Gelato.Tests: 18 pruebas
```

Las pruebas cubren el manifest, la identidad estable de versiones, la fusión de gemelas y los
casos borde del failover, sin necesitar un servidor.

## Créditos

Fork de **[lostb1t/Gelato](https://github.com/lostb1t/Gelato)**, bajo licencia **GPL-3.0**
(ver [LICENSE](LICENSE)). Gracias al autor original por la base y por mantener el proyecto
vivo; este fork solo agrega lo necesario para LAT-ADD.

Los cambios genéricos de este fork (manifest en forma corta, identidad de versión sin query)
están pensados para proponerse al proyecto original como pull requests.
