# Gelato-Lat — el fork de Gelato para LAT-ADD

Fork de [lostb1t/Gelato](https://github.com/lostb1t/Gelato) afinado para el addon de Stremio
**LAT-ADD** (catálogo directo, sin AIOStreams en el medio).

- **Parte de:** upstream `0.26.28` (commit `4e30d67`).
- **Requiere:** Jellyfin **12.1 o mayor** (el `targetAbi` sigue al original). Para un servidor
  en 12.0, actualizarlo primero — recomendado 12.2 (ver issue #1).
- **Mantiene el GUID del original a propósito**, para que la configuración (`Gelato.xml`) y
  los ítems ya importados se conserven al pasar de uno al otro.

## Instalación

1. **Quitar el repositorio del Gelato original** del servidor: *Panel → Plugins →
   Repositorios* → borrar la entrada de `lostb1t/Gelato`. Con ambos cargados y el mismo GUID,
   Jellyfin instala la versión más alta de cualquiera de los dos — y el original dejaría de
   leer el manifest de LAT-ADD (issue #2).
2. Añadir el repositorio del fork:
   `https://raw.githubusercontent.com/yisusm49-dot/Gelato-Lat/gh-pages/repository.json`
3. Instalar/actualizar **Gelato** desde ese repositorio.

## Versionado del fork

`<base-de-upstream>.<1000 + revisión del fork>`: sobre upstream `0.26.28`, el fork va
`0.26.28.1000`, `0.26.28.1001`, … Tras rebase sobre un upstream nuevo (p. ej. `0.26.30`), la
base sube: `0.26.30.1000`. Así la versión del fork es siempre mayor que la del upstream del
que parte y nunca puede chocar con la numeración del original.

## Valores recomendados para LAT-ADD (issue #6)

En la página de ajustes del plugin (tarjeta **LAT-ADD**, pestaña *Libraries*):

| Opción | Valor | Por qué |
|---|---|---|
| `ImportPeople` | `false` (o `PeopleLimit` 10–15) | Las personas son el mayor costo del importe (2788 personas por 99 películas medidas). |
| `LazyImages` | `true` | Las imágenes se bajan al verlas, no al importar. |
| `CatalogMaxItems` | `2000` | La biblioteca completa: 730 películas y 445 series. |
| `MetaConcurrency` | `4` | Los nodos del addon tienen 2 vCPU. |
| `PreferRelayRoute` | `true` | Ruta ⚡ primero, con el nodo como respaldo automático. |

Catálogos: activar solo `lat-add_movies` y `lat-add_series` (anime, kdrama y `recent_*` son
subconjuntos). `PlaybackFailover` activado para que el respaldo relay→nodo funcione.

## Qué cambia respecto al original

| Área | Cambio |
|---|---|
| Manifest (§2) | `resources` acepta forma corta (string) u objeto; un string hereda `types`/`idPrefixes` del manifest raíz. |
| Identidad (§4.1) | GUID estable por path `/media/<id>/<archivo>` sin firma; directo y ⚡ comparten versión. Solo para URLs `/media/` (#3). |
| Versiones (§3.1/§3.2) | Una versión por calidad, gemelas ⚡ fusionadas, relay primero con el nodo de respaldo, nombres de una línea sin emoji, clips ⛔ descartados. |
| Vencimiento (§4.2) | URL vencida → re-sync (máx. 10 s, #5) y reintento con firma fresca. |
| Import (§3.4) | `CatalogMaxItems` 2000, `LazyImages` on, `ImportPeople`/`PeopleLimit`, `MetaConcurrency`, reintentos con backoff ante 429/5xx. |
| Identificación (§4.3) | User-Agent `Gelato-LAT-ADD/<versión>`. |

Tests: `dotnet test` en `Gelato.Tests/` (18 pruebas).

## Mantenimiento

```
git fetch upstream
git rebase upstream/main lat-add
dotnet test        # si pasa, publicar: subir la base de versión y release
```
