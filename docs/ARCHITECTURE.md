# Arquitectura

Aplicación WinForms en C# para .NET Framework 4.8 (`PrintDeploy.csproj`, espacio de nombres
`InstaladorImpresoras`). Se compila a un único `PrintDeploy.exe` que pide elevación por manifiesto
(`app.manifest`, `requireAdministrator`). Las operaciones sobre el spooler las hace PowerShell 5.1, lanzado
por el `.exe`.

## Componentes

| Fichero | Responsabilidad |
|---|---|
| `Program.cs` | Punto de entrada (`Main`), captura de excepciones de UI y `Crash.Log` |
| `MainForm.cs` | Ventana principal, cabecera, página de progreso, ejecución del motor (`RunEngine`), colas de servidor (`RunShared`), asistente Canon (`RunCanon`), carpeta de trabajo segura |
| `MainForm.Pages.cs` | Página 02 instalar por IP (`ShowInstall`, `Detect`, `Go`), página 03 colas instaladas (`ShowQueues`) y `RunJobs` |
| `MainForm.TestPage.cs` | Página de prueba con seguimiento (`SendTestPage`, `Win32_PrintJob`, contadores del spooler, SNMP) |
| `ScannerForm.cs` | Ventana del escáner: directorio por oficinas, rango de red, credenciales de servidor |
| `NetworkScan.cs` | `Check` (ICMP, TCP 9100/631/515), `CheckShared` (SMB + `OpenPrinter`), `ParseRange`, `LocalRange`, `DetectOffice`, `Login` (sesión `IPC$`) |
| `PrinterProbe.cs` | Identificación: SNMP v2c con vuelta a v1, IPP, HTTP; contadores de páginas y avisos |
| `DriverCatalog.cs` | Marca por texto (`BrandOf`) y driver óptimo (`Resolve` → `DriverPlan`) |
| `RicohModels.cs` | Modelos Ricoh con driver propio y el universal de reserva |
| `PrinterDirectory.cs` | Lectura de `oficinas.txt` |
| `engine.ps1` | Motor: crea, reinstala y quita colas, puertos y drivers |
| `Theme.cs`, `ThemedDialog.cs`, `Organic.cs`, `NmapEye.cs`, `Logo.cs`, `PrinterMenu.cs` | Estilo, escala, diálogos, indicadores animados, logo y menú de impresoras |
| `FightLink.cs`, `FightArena.cs`, `Fighter.cs`, `FightSound.cs`, `Payload/fight-audio/` | Extra oculto, apagado por defecto (clic en el rombo del título del escáner). No interviene en la instalación |

## Flujo de una instalación

```
Escáner / Instalar por IP
  └─ PrinterProbe + NetworkScan → marca, modelo, descripción
       └─ DriverCatalog.Resolve(marca, descripción) → DriverPlan
            └─ MainForm.RunJobs(List<InstallJob>)
                 ├─ ExtractPayload: zip embebido → %ProgramData%\PrintDeploy\Work\{GUID}\
                 ├─ copia engine.ps1 embebido a esa carpeta
                 └─ RunEngine: powershell -File engine.ps1 -JobsB64 <trabajos>
                      └─ salida por líneas STEP|OK|WARN|ERR|LOG|SUMMARY → HandleEngineLine → registro y progreso
```

- **Trabajos** (`InstallJob`, `MainForm.Pages.cs`): `N` cola nueva en una IP, `E` reinstalar cola
  existente, `R` quitar. Si la IP ya tiene cola, `N` se convierte en reinstalar esa (no duplica).
- **Colas de servidor** no pasan por `engine.ps1`: `RunShared` genera un script con `Add-Printer
  -ConnectionName` (usuario actual) y `printui /ga` (todos los usuarios). Si falla con `0x80070705`,
  `0x80070709` o `0x800706BA`, invierte los DNS del adaptador con la ruta por defecto y reintenta
  (constante `AddPrinterWithDnsRetry`).
- **Canon con asistente**: `RunCanon` lanza el `Setup.exe` oficial del paquete en un proceso aparte.

## Motor (`engine.ps1`)

Entrada `-JobsB64`: base64 (UTF-8) de líneas separadas por tabulador, documentadas en la cabecera del
script. Reutiliza el puerto TCP/IP que ya apunte a la IP (`Get-IpPort`), activa SNMP en el puerto
(`Enable-PortSnmp`, comunidad `public`), comprueba cada cola tras crearla (`Test-Queue`) y deshace lo creado
si falla. Al quitar, borra el puerto y el driver solo si ninguna otra cola los usa (`Remove-UnusedPort`,
`Remove-UnusedDriver`). **No se ejecuta a mano**: depende de las rutas de INF que le pasa el `.exe`.

## Configuración

| Qué | Dónde | Cuándo se lee |
|---|---|---|
| Directorio de oficinas | `oficinas.txt` junto al `.exe` → `%ProgramData%\PrintDeploy\oficinas.txt` → embebido al compilar | Al abrir el escáner |
| Paquetes de driver | `Payload\*.zip`, embebidos si existen al compilar | Al instalar |
| Modelos Ricoh | `RicohModels.All` y `RicohModels.Universal` | En `DriverCatalog.Resolve` |
| Driver Canon, marcas | Constantes y `Brands` en `DriverCatalog.cs` | En `DriverCatalog.Resolve` |
| Escala de interfaz | Variable de entorno `PRINTDEPLOY_SCALE` (p. ej. `1.5`) | Al arrancar |

Los modelos y drivers viven en código y no en un fichero externo a propósito: un modelo nuevo necesita
su paquete embebido, así que de todas formas hay que recompilar.

## Ficheros que escribe en el equipo

| Ruta | Contenido |
|---|---|
| `%ProgramData%\PrintDeploy\Logs\instalacion_<fecha>.log` | Registro de cada sesión |
| `%ProgramData%\PrintDeploy\Work\{GUID}\` | Motor y drivers descomprimidos; ACL solo Administradores y SYSTEM, sin herencia |
| `%LocalAppData%\PrintDeploy\errores.log` | Excepciones con traza (se recorta a 256 KB) |
