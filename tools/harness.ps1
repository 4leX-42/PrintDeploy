param([string]$Exe, [string]$Mode, [string]$Spec)
# Prueba real de PrintDeploy: usa DriverCatalog, NetworkScan y MainForm.RunJobs del propio .exe.
#   -Mode plan  -Spec "ip|marca,ip|marca"           -> muestra el driver elegido
#   -Mode run   -Spec "K|nombre|ip|marca;..."       -> ejecuta los trabajos (K = N, E, R)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$bf = [Reflection.BindingFlags]"NonPublic,Public,Instance,Static"
$cat = $asm.GetType("InstaladorImpresoras.DriverCatalog")
$ns = $asm.GetType("InstaladorImpresoras.NetworkScan")

function Plan-For($ip, $brand) {
  $desc = ""
  if ($ip -and $ip -notmatch '^0\.') {
    $r = $ns.GetMethod("Check").Invoke($null, [object[]]@($ip, $false, [Threading.CancellationToken]::None)).GetAwaiter().GetResult()
    if ("$($r.State)" -eq "Printer") { $desc = $r.Description; if (-not $brand) { $brand = $r.Brand } }
  }
  $cat.GetMethod("Resolve").Invoke($null, [object[]]@([string]$brand, [string]$desc))
}

if ($Mode -eq "plan") {
  foreach ($s in $Spec -split ',') {
    $ip, $brand = $s -split '\|'
    $p = Plan-For $ip $brand
    "{0,-14} {1,-8} -> {2}  [Resource={3}]" -f $ip, $p.Brand, $p.Text, $p.Resource
  }
  return
}

$jt = $asm.GetType("InstaladorImpresoras.MainForm+InstallJob")
$list = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($jt))
foreach ($s in $Spec -split ';') {
  $k, $name, $ip, $brand = $s -split '\|'
  $j = [Activator]::CreateInstance($jt)
  $jt.GetField("Kind").SetValue($j, [char]$k)
  $jt.GetField("Name").SetValue($j, $name)
  $jt.GetField("Ip").SetValue($j, $ip)
  if ($k -ne 'R') { $jt.GetField("Plan").SetValue($j, (Plan-For $ip $brand)) }
  $list.Add($j)
}
$m = [Activator]::CreateInstance($asm.GetType("InstaladorImpresoras.MainForm"))
$m.StartPosition = "Manual"; $m.Location = New-Object Drawing.Point(-2000, -2000); $m.Show()
$task = $m.GetType().GetMethod("RunJobs", $bf).Invoke($m, [object[]]@($list, "Test"))
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $task.IsCompleted -and $sw.Elapsed.TotalMinutes -lt 10) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
for ($i = 0; $i -lt 20; $i++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
$log = $m.GetType().GetField("logPath", $bf).GetValue($m)
Get-Content $log | ForEach-Object { ($_ -replace '^\d\d:\d\d:\d\d ', '') }
"--- {0:N1} s" -f $sw.Elapsed.TotalSeconds
$m.GetType().GetField("closeAllowed", $bf).SetValue($m, $true)
$m.Close()

