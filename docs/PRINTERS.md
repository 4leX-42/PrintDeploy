# Impresoras y drivers

## Compatibilidad comprobada

Fuente de cada dato entre paréntesis. Lo que no aparece aquí no se ha validado.

| Modelo | Driver que elige PrintDeploy | Estado |
|---|---|---|
| Ricoh IM C4500 | `RICOH IM C4500 PCL 6` (`ricoh_c4500.zip`) | Instalada con la batería `tools/suite.ps1` y en uso en el entorno |
| Canon imageFORCE C5150/P | `Canon Generic Plus PCL6` (`canon.zip`) | Instalada y en uso en el entorno |
| Cola de servidor con Canon Generic Plus PCL6 | el del servidor | Conectada y en uso en el entorno |
| Canon imageFORCE C331/P, C1333 | `Canon Generic Plus PCL6` | Detectadas por el escáner; instalación sin validar |
| HP LaserJet E60155 | `HP Universal Printing PCL 6` si está instalado; si no, genérico | Detectada por el escáner; instalación sin validar |
| Ricoh IM C4510A | `RICOH IM C4510 PCL 6` (`ricoh.zip`) | Definido en `RicohModels.cs`; sin prueba documentada |

Las Canon del entorno solo responden SNMP v1: `PrinterProbe` prueba v2c y, si no hay respuesta, v1, y
recuerda la versión por IP.

## Elección de driver (`DriverCatalog.Resolve`)

1. Marca: la que dice el equipo, o `BrandOf` sobre la descripción SNMP / IPP / web.
2. Por marca:
   - **Ricoh**: modelo exacto si `RicohModels.Detect` lo reconoce; si no, el universal
     (`RICOH PCL6 UniversalDriver`).
   - **Canon**: `Canon Generic Plus PCL6`, instalado directamente (sin asistente: así se evita «Ya está
     instalada una versión posterior del controlador»).
   - **HP**: `HP Universal Printing PCL 6`, solo si ya está instalado en Windows (no va embebido).
3. Entre el driver instalado y el del paquete gana el de **mayor versión** (`DriverVer` del INF frente a
   `MSFT_PrinterDriver.DriverVersion`); a igual versión, el instalado.
4. Otras marcas (Kyocera, Xerox, Brother, Konica Minolta, Sharp, Lexmark, Epson, Toshiba, Samsung): su
   driver universal o PCL 6 si ya está instalado. En último caso, `Microsoft PCL6 Class Driver`, que imprime
   pero sin bandejas ni acabados y se avisa como genérico.

Reconocer la marca no significa haberla probado: esas marcas solo tienen la regla de detección.

## Paquetes de driver

No se publican (software de fabricante). Nombres, contenido y origen oficial de cada zip:
[`Payload/README.md`](../Payload/README.md). Si un zip falta al compilar, el `.exe` compila igual y avisa
al intentar usarlo.

## Añadir un modelo Ricoh con driver propio

1. Zip con la carpeta `disk1\` de su PCL 6 Driver en `Payload\` (los instaladores son autoextraíbles:
   `7z x paquete.exe`).
2. Entrada en `RicohModels.All` con `DriverName` **exacto del INF**, `Resource` (zip), `Inf` (ruta dentro del
   zip) y `Match` (expresión que lo reconoce en la descripción SNMP o en el nombre del driver).
3. `EmbeddedResource` del zip en `PrintDeploy.csproj` con `LogicalName` igual al nombre del zip.
4. Si cambia la versión del universal, actualizar `RicohModels.Universal.DriverName`
   (`RICOH PCL6 UniversalDriver V4.xx`).

## Añadir otra marca

- Solo detección: añadir su expresión a `DriverCatalog.Brands`.
- Con driver embebido: nuevo `case` en `Resolve` con `Newest(...)`, igual que Canon, y su `EmbeddedResource`.

## Validar un modelo

1. `tools/harness.ps1 -Mode plan -Spec "<ip>|"` muestra el driver que elegiría, sin instalar nada.
2. Instalar desde la app o con `tools/harness.ps1 -Mode run` usando una cola de prueba (`PD-TEST*`).
3. Página de prueba desde la app: confirmar que sube el contador de páginas de la impresora.
4. Quitar la cola de prueba y anotar el resultado en la tabla de arriba.
