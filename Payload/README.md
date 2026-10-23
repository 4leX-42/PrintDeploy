# Payload — paquetes de driver

Los drivers de Canon y Ricoh **no se incluyen en el repositorio**. Descárguelos de la web oficial del fabricante, compruebe la firma digital y colóquelos aquí con estos nombres antes de compilar. Cada zip que falte simplemente no se embebe; PrintDeploy avisa si se intenta usar.

| Fichero | Contenido | Origen oficial |
|---|---|---|
| `ricoh.zip` | carpeta `disk1\` del **PCL 6 Driver** de IM C4510 (INF `disk1\oemsetup.inf`, driver `RICOH IM C4510 PCL 6`) | support.ricoh.com → IM C4510/C5510/C6010 → Windows 11 64-bit → PCL 6 Driver |
| `ricoh_c4500.zip` | carpeta `disk1\` del **PCL 6 Driver** de IM C4500 (INF `disk1\MPC4500_.inf`, driver `RICOH IM C4500 PCL 6`) | support.ricoh.com → IM C4500/C5500/C6000 → Windows 11 64-bit → PCL 6 Driver |
| `ricoh_universal.zip` | carpeta `disk1\` del **PCL6 Driver for Universal Print** (INF `disk1\oemsetup.inf`, driver `RICOH PCL6 UniversalDriver V4.46`) | support.ricoh.com → cualquier modelo → PCL6 Driver for Universal Print |
| `canon.zip` | contenido completo del paquete **Canon Generic Plus PCL6** x64 (con `Setup.exe` en la raíz) | canon.es → Soporte → Generic Plus PCL6 |

Los instaladores de Ricoh son autoextraíbles: se abren con 7-Zip (`7z x paquete.exe`) y se comprime la carpeta `disk1`.

Si cambia la versión del driver universal, actualice `DriverName` en `RicohModels.cs` (`RICOH PCL6 UniversalDriver V4.xx`).
