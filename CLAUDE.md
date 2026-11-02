# PrintDeploy — reglas para asistentes de programación

Herramienta WinForms (.NET Framework 4.8, C#) que se compila a un único `PrintDeploy.exe` elevado. Instala,
reinstala y quita impresoras de red con el driver óptimo. Mapa del código: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Antes de tocar nada

- Leer [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Si el cambio toca drivers o modelos, también
  [docs/PRINTERS.md](docs/PRINTERS.md).
- Puntos de entrada: `Program.Main` → `MainForm`. Instalación: `DriverCatalog.Resolve` → `MainForm.RunJobs` →
  `engine.ps1`. Colas de servidor: `MainForm.RunShared`.

## Comandos

```powershell
dotnet build -c Release                                          # compilar
powershell -STA -File tools\harness.ps1 -Exe <exe> -Mode plan -Spec "<ip>|<marca>"   # driver elegido, sin instalar
powershell -STA -File tools\suite.ps1 -Exe <exe> -Dir <carpeta> -Ip <ip ricoh>       # batería completa (instala y deshace)
```

Detalle y reglas de prueba: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Restricciones técnicas

- El motor y los scripts generados corren en **Windows PowerShell 5.1**: nada de sintaxis de PowerShell 7.
- La salida del motor es un protocolo por líneas (`STEP|pct|texto`, `OK|`, `WARN|`, `ERR|`, `LOG|`,
  `SUMMARY|`) que interpreta `MainForm.HandleEngineLine`. No escribir otra cosa por stdout.
- Los paquetes de `Payload\*.zip` se embeben solo si existen (`Condition="Exists(...)"` en el `.csproj`).
  El código debe seguir funcionando sin ellos.
- La carpeta de trabajo `%ProgramData%\PrintDeploy\Work\{GUID}` se crea con ACL solo para Administradores y
  SYSTEM (`SecureWorkDir`). No moverla a `%TEMP%`: lo que contiene se ejecuta elevado.
- `DriverName` de un modelo tiene que ser exactamente el del INF.
- UI: estilo propio (carbón + dorado, rojo solo para errores), `ThemedDialog` en lugar de `MessageBox`,
  medidas a través de `Theme.S(...)` para respetar la escala.

## Reglas de cambio

- No afirmar compatibilidad de un modelo sin prueba: actualizar la tabla de `docs/PRINTERS.md` solo con
  resultados reales.
- Nunca versionar `oficinas.txt`, `RESUMEN.md`, `Payload\*.zip` ni IPs, nombres de oficinas o servidores
  reales. El ejemplo va en `oficinas.example.txt` con datos ficticios.
- Pruebas solo con colas `PD-TEST*`; nunca con colas reales de usuarios.
- Cambios de comportamiento: actualizar el documento de `docs/` afectado en el mismo cambio.
- Sin atribuciones de asistentes en commits, PR ni ficheros.
