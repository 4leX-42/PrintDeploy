<!--
[ES] PrintDeploy: detecta marca y modelo de cualquier impresora de red e instala el driver más reciente y adecuado; escáner, colas y servidor.
[EN] PrintDeploy: detects brand and model of any network printer and installs the newest suitable driver; scanner, queues and server.
Contexto: admin
-->
# PrintDeploy

Aplicación Windows (WinForms, .NET Framework 4.8) para desplegar y reparar impresoras. Un único `.exe` portátil que pide elevación al abrir.

## Qué hace

**01 · Scanner** (tecla `1` o botón `SCANNER`)

- Directorio de oficinas (`oficinas.txt`) + rango de red (por defecto el /24 del equipo; acepta `192.168.4.1-254`, `192.168.4.0/24` o `IP-IP`, máx. 1024). Un /24 en ~2 s.
- Por IP: ICMP + TCP 9100 (RAW), 631 (IPP), 515 (LPD; solo cuenta si además se identifica como impresora). Host sin respuesta: no se consulta más.
- Identificación: una consulta SNMP v2c (v1 de reserva) con `hrDeviceDescr` / `sysDescr`, `sysName` y `sysLocation`; IPP y HTTP solo si SNMP no basta. Modelo sin firmware ni sufijos.
- Colas de servidor `\\servidor\cola`: TCP 445 (SMB) + `OpenPrinter` / `GetPrinter` nivel 2: driver, estado, ubicación, comentario y puerto (pool de puertos, puerto `nul` de retención PaperCut). Distingue servidor caído, SMB cerrado, cola inexistente (1801) y sin permiso (5 / 1326).
- Sin permiso: **Instalar** pasa a **Credenciales**. Sesión `IPC$` con usuario del servidor (valida al momento) + credencial en el Administrador de credenciales de Windows; recomprueba las colas de ese servidor.
- Nombre legible: `PROCESAL_DCH` + `C/ Mayor 1 Plt3 Derecha` → *Procesal Dcha · P3*. Nombres de fábrica (modelo, derivados de la MAC) ignorados.
- Ubicación: oficina con una impresora en el mismo /24 que el equipo; si no, la del sitio de Active Directory. Esa oficina arriba y abierta; el resto plegadas al final.
- Indicador: ojo de fuego (estilo Ojo de Sauron) generado por píxel con ruido fractal 3D: azul = ya instalada en este equipo (con «Instalada · cola» y botón Reinstalar), verde = online, rojo = offline / sin impresión / cola inaccesible, dorado = comprobando, brasa gris = pendiente. Cada ojo aviva sus llamas cada 8-16 s al azar (llamarada de 1,2 s); fotogramas en caché, un solo temporizador.
- **Instalar** (doble clic / Enter) cualquier impresora detectada, con el driver de *Elección de driver*. Si ya hay cola en esa IP, se reinstala. Cola de servidor: `Add-Printer -ConnectionName` (usuario actual) + `printui /ga` (todos los usuarios).
- **Scan** / `F5`: rango (**Stop** para pararlo). **Recheck offices**: directorio. Botón en relieve con el ojo del logo de Nmap en dorado (iris con retícula que gira, mira en cruz) y haz de faro de izquierda a derecha.

**02 · Instalar por IP** (cualquier marca)

- Detección al escribir la IP: fabricante, modelo, nombre y ubicación SNMP, puertos. Nombre de cola sugerido.
- Muestra el driver elegido (versión y origen) y si la IP ya tiene cola (se reinstala esa, sin duplicar).
- **Asistente Canon** (instalador oficial) disponible si la impresora es Canon; corre aparte y cerrarlo no cierra PrintDeploy.

**03 · Impresoras instaladas**

- Todas las colas de red del equipo, con indicador verde / rojo (la IP del puerto responde como impresora; colas de servidor: la cola se abre).
- **Reinstalar**: modelo por la IP de cada cola (si no responde, por su driver / nombre actual) y driver óptimo. Conserva nombre, puerto, predeterminada, compartida, ubicación y comentario.
- **Quitar**: cola, puerto TCP/IP y driver si nadie más los usa. Conexiones de servidor: también la conexión global (`printui /gd`).

**Elección de driver** (`DriverCatalog.cs`): siempre la versión más reciente entre la instalada en Windows y la del paquete embebido (`DriverVer` del INF).

| Marca | Driver |
|---|---|
| Ricoh | modelo exacto (IM C4510A, IM C4500); si no, **RICOH PCL6 UniversalDriver** más reciente |
| Canon | **Canon Generic Plus PCL6** (sin asistente: evita «Ya está instalada una versión posterior del controlador») |
| HP | **HP Universal Printing PCL 6** instalado |
| Otras (Kyocera, Xerox, Brother, Konica, Sharp…) | su driver universal / PCL instalado; si no, **Microsoft PCL6 Class Driver** (genérico, avisado) |

**Motor** (`engine.ps1`): reutiliza el puerto TCP/IP que ya apunte a la IP, activa SNMP (bandejas), comprueba cada cola tras crearla y deshace lo creado si falla. Un driver que falla no detiene el resto: sus colas vuelven al driver anterior. Los drivers antiguos se limpian al final, nunca los de Windows ni los que use otra cola.

**General**

- Portátil: un solo `PrintDeploy.exe` (drivers y directorio embebidos; .NET Framework 4.8 incluido en Windows 10/11). Funciona desde USB o carpeta de red.
- Escala: toda la interfaz (medidas y fuentes) sigue el escalado de Windows con la misma proporción; se reduce si la pantalla es pequeña. `PRINTDEPLOY_SCALE` la fuerza.
- Teclas: `1` scanner · `2` instalar · `3` colas · `F5` reescanear · `Esc` volver / salir.
- Tras instalar una impresora: **Impresoras y escáneres** (Configuración de Windows, con el historial) y **Página de prueba** con seguimiento: entrega por Windows (cola y contadores del spooler) y confirmación física por SNMP en la impresora (su contador de páginas sube; si no, sus avisos: sin papel, atasco, tapa abierta…). Canon responde solo SNMP v1; se recuerda la versión por IP.
- Al terminar una instalación, **Volver** regresa al inicio o al scanner; la app no se cierra.
- Icono de abrir junto al de impresora: menú con las impresoras instaladas; clic = su cola de impresión, submenú = propiedades / preferencias (`printui`).
- Icono de impresora en las cabeceras → *Dispositivos e impresoras* (`shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}`).
- Registro de cada ejecución en `%ProgramData%\PrintDeploy\Logs\`.

Parte de [GADGETS-SUPPORT_CX](https://github.com/4leX-42/GADGETS-SUPPORT_CX), el catálogo de herramientas.

## Compilar

Requiere el SDK de .NET (cualquiera reciente; descarga las *reference assemblies* de net48 desde nuget.org).

```powershell
dotnet build -c Release .\PrintDeploy.csproj
# -> bin\Release\net48\PrintDeploy.exe
```

Lo que **no** está en el repo y se añade antes de compilar:

| Qué | Dónde | Por qué |
|---|---|---|
| Paquetes de driver Canon/Ricoh | `Payload\*.zip` — ver [`Payload/README.md`](Payload/README.md) | Software de fabricante, no se redistribuye |
| Directorio de oficinas | `oficinas.txt` — formato en [`oficinas.example.txt`](oficinas.example.txt) | IPs internas |

Sin ellos compila igualmente: el `.exe` avisa del paquete que falta y el directorio puede ir como `oficinas.txt` junto al `.exe` o en `%ProgramData%\PrintDeploy\`.

## Ficheros

| Fichero | Para qué |
|---|---|
| `MainForm.cs` | Ventana, cabecera, progreso, motor, instalación desde el escáner, colas de servidor, asistente Canon |
| `MainForm.Pages.cs` | Páginas 02 (instalar por IP) y 03 (colas instaladas) y lanzador de trabajos |
| `MainForm.TestPage.cs` | Página de prueba con seguimiento del trabajo (Win32_PrintJob y contadores del spooler) |
| `DriverCatalog.cs` | Marca y driver óptimo por impresora; versiones instaladas frente a las del paquete |
| `ScannerForm.cs` | Ventana del escáner (grupos por oficina, filas desplegables, credenciales) |
| `NetworkScan.cs` | Escáner: ping, puertos, identificación, colas de servidor, ubicación, rangos, credenciales |
| `PrinterProbe.cs` | Detección del dispositivo y modelo (SNMP, IPP, HTTP) |
| `RicohModels.cs` | Modelos Ricoh soportados, driver y paquete; universal de reserva |
| `PrinterDirectory.cs` | Lectura de `oficinas.txt` |
| `ThemedDialog.cs` | Avisos, confirmaciones y credenciales con el estilo de la app |
| `Theme.cs` | Estilo (carbón, blanco, dorado; rojo solo para errores), escala única de interfaz y controles dibujados |
| `NmapEye.cs` | Ojo del botón Escáner calcado del logo de Nmap (dorado): párpados rayados, iris con retícula que gira, mira |
| `Logo.cs` | Emblema de la cabecera (`Assets/logo.png` embebido, escalado y en caché) |
| `Assets/logo.png`, `app.ico` | Logo: emblema dorado (cabecera) e icono del `.exe` (placa negra) |
| `Organic.cs` | Ojos de fuego de estado (shader por píxel, caché de fotogramas, llamaradas al azar) |
| `TentacleLink.cs` | Tentáculo cyberpunk (metal negro, vértebras y núcleo de energía dorados) que une la ventana principal y el escáner, abierto a su derecha (ventana por capas, sin clics) |
| `engine.ps1` | Motor: instala, reinstala y quita colas de cualquier marca; lo lanza el `.exe` |

## Añadir un modelo Ricoh

1. Zip con la carpeta `disk1\` del PCL 6 Driver en `Payload\`.
2. Entrada en `RicohModels.All` con su `DriverName` exacto (el del INF), el zip, el INF y la expresión que lo reconoce.
3. `EmbeddedResource` del zip en `PrintDeploy.csproj`.

---

**EN** — Portable WinForms (.NET Framework 4.8) tool that scans the network for printers (ICMP, TCP 9100/631/515, SNMP, SMB for server queues), shows online/offline per office with the local office first, and installs Ricoh (model-specific or universal driver, rebuilding existing queues), Canon Generic Plus PCL6 (direct TCP/IP port and queue, no vendor wizard) and shared server queues (with optional server credentials). Vendor driver packages and the office directory are not in the repo: see `Payload/README.md` and `oficinas.example.txt`. Build with `dotnet build -c Release`.
