# Bateria de pruebas por reflexion (ver docs/lecciones/pruebas.md del lab).
# -Ip: una impresora Ricoh real de la red para la instalacion de prueba (cola PD-TEST-OPT, se quita al final).
param([string]$Exe, [string]$Dir, [Parameter(Mandatory)][string]$Ip)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::SetUnhandledExceptionMode([Windows.Forms.UnhandledExceptionMode]::CatchException)
[Windows.Forms.Application]::add_ThreadException({ param($s, $e) $script:fails++; Write-Host "  EXCEPCION UI: $($e.Exception.GetType().Name): $($e.Exception.Message)`n$($e.Exception.StackTrace)" })
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$bf = [Reflection.BindingFlags]"NonPublic,Public,Instance,Static"
$fails = 0
function Pump($ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { if ($script:wctx) { [Threading.SynchronizationContext]::SetSynchronizationContext($script:wctx) }; [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } }
function WaitTask($t, $max = 300000) { $sw = [Diagnostics.Stopwatch]::StartNew(); while (-not $t.IsCompleted -and $sw.ElapsedMilliseconds -lt $max) { [Threading.SynchronizationContext]::SetSynchronizationContext($script:wctx); [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }; if ($t.Status -ne "RanToCompletion") { "  TAREA: $($t.Status) $($t.Exception.InnerException)" } }
function Check($name, $ok, $detail = "") { if ($ok) { "  OK   $name $detail" } else { $script:fails++; "  FALLO $name $detail" } }
function Snap($f, $path) { $b = New-Object Drawing.Bitmap([int]$f.Width, [int]$f.Height); $f.DrawToBitmap($b, [Drawing.Rectangle]::new(0, 0, $f.Width, $f.Height)); $b.Save($path); $b.Dispose() }
function Printers { @(Get-Printer | Select-Object -ExpandProperty Name | Sort-Object) -join ' | ' }
function Ports { @(Get-PrinterPort | Select-Object -ExpandProperty Name | Sort-Object) -join ' | ' }

$basePrinters = Printers; $basePorts = Ports
"BASE impresoras: $basePrinters"

$m = [Activator]::CreateInstance($asm.GetType("InstaladorImpresoras.MainForm")); $m.Show(); Pump 400
$t = $m.GetType()
$wctx = New-Object Windows.Forms.WindowsFormsSynchronizationContext
function Ctx { [Threading.SynchronizationContext]::SetSynchronizationContext($wctx) }
"1. Inicio"
$menuCards = @($t.GetField("body", $bf).GetValue($m).Controls | ForEach-Object { $_.Controls } | Where-Object { $_.GetType().Name -eq "Card" })
Check "3 entradas en el menu" ($menuCards.Count -eq 3) "($($menuCards.Count))"

"2. Scanner"
Ctx; $t.GetMethod("ShowScanner", $bf).Invoke($m, @()) | Out-Null
$s = $t.GetField("scanner", $bf).GetValue($m)
Pump 9000
$list = $s.GetType().GetField("list", $bf).GetValue($s)
$items = @($list.Items)
$det = @($items | Where-Object { $_.Detected }).Count; $inst = @($items | Where-Object { $_.Installed }).Count; $fail = @($items | Where-Object { $_.Failed }).Count
Check "entradas escaneadas" ($items.Count -gt 0) "($($items.Count): $det online, $inst instaladas, $fail offline)"
Check "deteccion de instaladas (ojo azul)" ($inst -gt 0)
$link = $null
Snap $s "$Dir\suite_scanner.png"
$s.Hide(); Pump 200

"3. Instalar por IP ($Ip)"
Ctx; $t.GetMethod("ShowInstall", $bf).Invoke($m, @()) | Out-Null; Pump 200
function Boxes($c) { foreach ($x in $c.Controls) { if ($x -is [Windows.Forms.TextBox]) { $x }; Boxes $x } }
$ipBox = @(Boxes $m)[0]; $ipBox.Text = $Ip; Pump 7000
$nameBox = @(Boxes $m)[1]
Check "nombre de cola sugerido" ($nameBox.Text.Length -gt 0) "«$($nameBox.Text)»"
Snap $m "$Dir\suite_install.png"

"4. Impresoras instaladas"
Ctx; $t.GetMethod("ShowQueues", $bf).Invoke($m, @()) | Out-Null; Pump 6000
$rows = @($t.GetField("body", $bf).GetValue($m).Controls | ForEach-Object { $_.Controls } | Where-Object { $_.GetType().Name -eq "CheckRow" -and -not $_.Header })
Check "filas de impresoras" ($rows.Count -gt 0) "($($rows.Count))"

"5. Instalacion real PD-TEST-OPT en $Ip"
$dc = $asm.GetType("InstaladorImpresoras.DriverCatalog")
$plan = $dc.GetMethod("Resolve").Invoke($null, [object[]]@("Ricoh", "RICOH IM C4500"))
"  driver: $($plan.Text)"
$jt = $asm.GetType("InstaladorImpresoras.MainForm+InstallJob")
function Job($kind, $name, $ip) { $j = [Activator]::CreateInstance($jt); $jt.GetField("Kind").SetValue($j, [char]$kind); $jt.GetField("Name").SetValue($j, $name); if ($ip) { $jt.GetField("Ip").SetValue($j, $ip) }; if ($kind -ne 'R') { $jt.GetField("Plan").SetValue($j, $plan) }; $j }
function Jobs($j) { $l = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($jt)); $l.Add($j); , $l }
$run = $t.GetMethod("RunJobs", $bf)
$sw = [Diagnostics.Stopwatch]::StartNew()
Ctx; WaitTask ($run.Invoke($m, [object[]]@((Jobs (Job 'N' 'PD-TEST-OPT' $Ip)), "Ricoh")))
$t1 = $sw.Elapsed.TotalSeconds
$p = Get-Printer -Name "PD-TEST-OPT" -ErrorAction SilentlyContinue
Check "cola creada" ($p -ne $null) ("en {0:N1} s, driver {1}, puerto {2}" -f $t1, $p.DriverName, $p.PortName)
Check "estado Normal" ($p.PrinterStatus -eq 'Normal') "($($p.PrinterStatus))"
Check "accesos tras instalar visibles" ($t.GetField("lnkTest", $bf).GetValue($m).Visible -and $t.GetField("installedPrinter", $bf).GetValue($m) -eq "PD-TEST-OPT")
Snap $m "$Dir\suite_done.png"
$wd = $t.GetField("workDir", $bf).GetValue($m)
Check "paquete extraido en la carpeta de trabajo" (Test-Path "$wd\ricoh_c4500\*") "($wd)"
$who = @((Get-Acl $wd).Access | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique)
Check "carpeta solo Administradores (S-1-5-32-544) y SYSTEM (S-1-5-18)" (($who -join ',') -eq 'S-1-5-18,S-1-5-32-544') "($($who -join ', '); heredado: $((Get-Acl $wd).AreAccessRulesProtected -eq $false))"
Check "sin marca .ok en disco" (-not (Test-Path "$wd\ricoh_c4500\.ok"))

"6. Reinstalar (misma IP: el motor reinstala la cola, reutiliza el paquete extraido)"
$sw.Restart()
Ctx; WaitTask ($run.Invoke($m, [object[]]@((Jobs (Job 'N' 'PD-TEST-OPT-2' $Ip)), "Ricoh")))
$t2 = $sw.Elapsed.TotalSeconds
Check "sin cola duplicada" ((Get-Printer -Name "PD-TEST-OPT-2" -ErrorAction SilentlyContinue) -eq $null) ("en {0:N1} s" -f $t2)
Check "la cola original sigue" ((Get-Printer -Name "PD-TEST-OPT" -ErrorAction SilentlyContinue) -ne $null)

"7. Quitar"
$sw.Restart()
Ctx; WaitTask ($run.Invoke($m, [object[]]@((Jobs (Job 'R' 'PD-TEST-OPT' $null)), "Colas")))
Check "cola eliminada" ((Get-Printer -Name "PD-TEST-OPT" -ErrorAction SilentlyContinue) -eq $null) ("en {0:N1} s" -f $sw.Elapsed.TotalSeconds)

"8. Pagina de prueba con seguimiento (cola temporal en NUL:, sin papel)"
if (-not (Get-PrinterPort -Name "NUL:" -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name "NUL:" }
Add-Printer -Name "PD-TEST-NUL" -DriverName "Microsoft PCL6 Class Driver" -PortName "NUL:"
try {
  Ctx; $t.GetMethod("ShowProgress", $bf).Invoke($m, [object[]]@("Prueba", "Instalando", "x")) | Out-Null
  $t.GetField("installedPrinter", $bf).SetValue($m, "PD-TEST-NUL")
  Ctx; $t.GetMethod("Finish", $bf).Invoke($m, [object[]]@($true, $null)) | Out-Null
  Ctx; $t.GetMethod("SendTestPage", $bf).Invoke($m, @()) | Out-Null
  $sw.Restart(); while ($sw.Elapsed.TotalSeconds -lt 100 -and $t.GetField("trackingTest", $bf).GetValue($m)) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
  $st = $t.GetField("lblStep", $bf).GetValue($m).Text
  Check "pagina de prueba confirmada" ($st -match "correctamente") ("({0:N1} s: $st)" -f $sw.Elapsed.TotalSeconds)
} finally {
  Get-PrintJob -PrinterName "PD-TEST-NUL" -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
  Remove-Printer -Name "PD-TEST-NUL" -ErrorAction SilentlyContinue
}

"9. Estado final"
Pump 500
$s.Dispose(); $m.Dispose()
Remove-Item $wd -Recurse -Force -ErrorAction SilentlyContinue
$endPrinters = Printers; $endPorts = Ports
Check "impresoras = inicio" ($endPrinters -eq $basePrinters) $(if ($endPrinters -ne $basePrinters) { "FIN: $endPrinters" })
Check "puertos = inicio" ($endPorts -eq $basePorts) $(if ($endPorts -ne $basePorts) { "FIN: $endPorts | INICIO: $basePorts" })
""
"RESULTADO: $fails fallos"
