# Desarrollo y pruebas

## Requisitos

- Windows 10/11 con .NET Framework 4.8 (viene con el sistema).
- SDK de .NET reciente. `nuget.config` fija nuget.org como única fuente para descargar las *reference
  assemblies* de net48.
- PowerShell 5.1 (Windows PowerShell): lo usan el motor y las pruebas.

## Compilar

```powershell
dotnet build -c Release                                           # completo -> bin\Release\net48\PrintDeploy.exe
dotnet build -c Release -p:EmbedPayload=false -o bin\portable     # portable -> bin\portable\PrintDeploy.exe + Payload\
```

| Variante | Qué sale | Cuándo |
|---|---|---|
| Completa | Un único `.exe` de unos 180 MB con los drivers dentro | Un solo fichero que copiar |
| Portable | `.exe` de unos 2,6 MB y los zip en `Payload\` junto a él; se distribuye la carpeta entera | Arranque rápido: el antivirus y el control de cuentas no tienen que leer 180 MB, y desde USB o carpeta de red solo se lee el driver que se instala |

Medido en el mismo equipo, primer arranque de un fichero nuevo: 779 ms la completa, 118 ms la portable
(los siguientes, unos 90 ms las dos).

El `.exe` lleva dentro la huella SHA-256 de cada zip que había en `Payload\` al compilar (recurso
`payload.sha256`, objetivo `PayloadManifest` del `.csproj`). La portable solo usa un zip externo si su
huella coincide, porque su contenido se instala elevado: **el `.exe` y su carpeta `Payload\` tienen que
salir de la misma compilación**. Cambiar un zip obliga a recompilar.

Antes de compilar, lo que no está en git:

| Qué | Dónde | Sin él |
|---|---|---|
| Paquetes de driver | `Payload\*.zip` ([Payload/README.md](../Payload/README.md)) | Compila; el `.exe` avisa al intentar usar el paquete |
| Directorio de oficinas | `oficinas.txt` ([formato](../oficinas.example.txt)) | Compila; el escáner muestra solo la red local, o lee `oficinas.txt` junto al `.exe` |

**Si PrintDeploy está abierto, el `.exe` está bloqueado**: compilar con `-o <otra carpeta>` o cerrar la app
antes.

## Pruebas

No hay proyecto de pruebas unitarias ni CI. Las pruebas cargan el `.exe` compilado por reflexión desde
PowerShell y llaman a sus métodos.

| Script | Qué hace | Toca el sistema |
|---|---|---|
| `tools/harness.ps1 -Exe <exe> -Mode plan -Spec "ip\|marca,..."` | Driver elegido para cada impresora | No |
| `tools/harness.ps1 -Exe <exe> -Mode run -Spec "N\|nombre\|ip\|marca;..."` | Ejecuta trabajos reales (`N`, `E`, `R`) | Sí |
| `tools/suite.ps1 -Exe <exe> -Dir <carpeta> -Ip <ricoh>` | Batería completa: menú, escáner, instalar por IP, colas, instalación real `PD-TEST-OPT`, reinstalación sin duplicar, quitar, página de prueba en una cola `NUL:` y comparación del estado final con el inicial | Sí; deja el estado igual |
| `tools/snmptest.cs` | Consultas SNMP sueltas con `PrinterProbe` | No |
| `tools/logoprep.cs` | Genera `app.ico` y `Assets/logo.png` desde la imagen del logo | No |

Ejecutar con `powershell.exe -STA -ExecutionPolicy Bypass -File ...` desde una consola elevada.

Reglas al probar:

- `suite.ps1` solo vale con **0 fallos y estado final igual al inicial** (impresoras y puertos).
- Usar colas con prefijo `PD-TEST*`. No usar colas reales de usuarios.
- Desde PowerShell, fijar `WindowsFormsSynchronizationContext` antes de cada llamada y dentro del bucle de
  `DoEvents` (como hacen `Pump` y `WaitTask` en `suite.ps1`): sin él, el código que sigue a un `await` toca
  la interfaz desde otro hilo y se cuelga.
- `Add-Type` de Windows PowerShell compila C# 5: nada de `$"..."` ni miembros con `=>`.

## Criterios antes de publicar

1. `dotnet build -c Release` sin errores.
2. `harness.ps1 -Mode plan` con las marcas afectadas por el cambio.
3. `suite.ps1` si el cambio toca escáner, instalación, motor o página de prueba.
4. Revisar que el diff no lleva IPs internas, nombres de oficinas ni `oficinas.txt` / `RESUMEN.md`
   (ambos en `.gitignore`).

## Depuración

- Registro de la sesión: `%ProgramData%\PrintDeploy\Logs\instalacion_<fecha>.log`. Recoge todas las líneas
  del motor y los errores de PowerShell.
- Excepciones de la app con traza: `%LocalAppData%\PrintDeploy\errores.log`.
- Para ver qué haría el motor sin instalar, `harness.ps1 -Mode plan`.
