# Problemas frecuentes

Primero, el registro: botón **Ver registro** al terminar, o `%ProgramData%\PrintDeploy\Logs\`.

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| `Add-Printer` con `HRESULT 0x80070705` al conectar una cola de servidor | Resolución de nombres: el equipo no llega bien al servidor con sus DNS actuales | Automático: PrintDeploy invierte los DNS del adaptador activo (manuales a DHCP, DHCP a `1.1.1.1` / `1.0.0.1`) y reintenta. El registro dice qué DNS había y cuáles quedan. Si sigue fallando, los devuelve como estaban |
| `0x80070709` o `0x800706BA` al conectar a servidor | Nombre de cola no válido o servidor RPC (Remote Procedure Call) inaccesible; a menudo, también DNS | Mismo reintento automático. Si persiste: comprobar que la cola existe y que el puerto 445 del servidor responde |
| Cola de servidor «sin permiso» (5 / 1326) | El usuario de Windows no tiene acceso a la cola | En el escáner, **Credenciales**: abre sesión `IPC$` con un usuario del servidor y la guarda en el Administrador de credenciales |
| Cola de servidor «no existe» (1801) | Nombre de la cola mal escrito en `oficinas.txt` | Corregir el directorio |
| «Esta compilación no incluye el paquete de driver ...» | El zip no estaba en `Payload\` al compilar | Añadirlo según [Payload/README.md](../Payload/README.md) y recompilar |
| Canon: «Ya está instalada una versión posterior del controlador» | El asistente oficial se niega a bajar de versión | Usar **Instalar** normal (sin asistente): instala el Generic Plus PCL6 directamente |
| Driver «genérico» en el plan | Marca sin driver embebido ni universal instalado | Imprime, pero sin bandejas ni acabados. Instalar el driver universal de la marca y repetir |
| Página de prueba «entregada» pero no sale hoja | Retenida por impresión segura o ID de departamento, o la impresora está calentando | Mirar el aviso SNMP que muestra la app; liberar el trabajo en la impresora |
| Canon no responde al escáner por SNMP | Solo admite SNMP v1 | Automático: `PrinterProbe` reintenta con v1 y lo recuerda por IP |
| Texto del registro con caracteres rotos (`operaci?n`) | Codificación de la salida de PowerShell | Corregido para colas de servidor en el script de `RunShared` (salida UTF-8) |
| La compilación falla con el `.exe` en uso | PrintDeploy abierto | Cerrarlo o compilar con `-o <otra carpeta>` |
