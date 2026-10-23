<#
.SYNOPSIS
    [ES] Motor de PrintDeploy: instala, reinstala y quita colas de impresora de cualquier marca.
    [EN] PrintDeploy engine: installs, reinstalls and removes printer queues of any brand.

.DESCRIPTION
    Lo lanza PrintDeploy.exe (ya elevado); no esta pensado para ejecutarse a mano.
    Entrada -JobsB64: base64(UTF8) de lineas separadas por tabulador:
      E <tab> nombre <tab> driver <tab> inf            reinstalar cola existente (mismo nombre y puerto)
      N <tab> nombre <tab> driver <tab> inf <tab> ip   cola nueva en esa IP (si ya hay una en esa IP, se reinstala esa)
      R <tab> nombre                                   quitar cola, su puerto y su driver si nadie mas los usa
    inf vacio = driver ya instalado o de Windows (no se toca el almacen de drivers).
    Salida por lineas para la interfaz:
      STEP|<porcentaje>|<texto>   OK|..  WARN|..  ERR|..  LOG|..  SUMMARY|..

.NOTES
    Contexto: admin
    PowerShell 5.1.
#>
param([Parameter(Mandatory)] [string]$JobsB64)

$ErrorActionPreference = 'Stop'
# sin barras de progreso: con la salida redirigida llegan como CLIXML por el canal de errores
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Out-Line($kind, $text) { [Console]::Out.WriteLine("$kind|$text"); [Console]::Out.Flush() }
function Step($pct, $text) { Out-Line 'STEP' "$pct|$text" }

function Enable-PortSnmp($portName) {
    $key = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\$portName"
    if (-not (Test-Path $key)) { return $false }
    Set-ItemProperty $key -Name 'SNMP Enabled' -Value 1 -Type DWord
    Set-ItemProperty $key -Name 'SNMP Index' -Value 1 -Type DWord
    Set-ItemProperty $key -Name 'SNMP Community' -Value 'public'
    $true
}

# Puerto TCP/IP para una IP: reutiliza el que ya apunte a ella (sea cual sea su nombre) para no duplicar
function Get-IpPort($ip) {
    $p = Get-PrinterPort | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1
    if ($p) { return $p.Name }
    $name = "IP_$ip"
    Add-PrinterPort -Name $name -PrinterHostAddress $ip
    Out-Line 'OK' "Puerto creado: $name"
    $name
}

function Remove-UnusedDriver($name) {
    if (-not $name) { return }
    if ($name -match '^Microsoft|Class Driver$|^Universal Print Class') { Out-Line 'LOG' "Driver de Windows conservado: $name"; return }
    if (Get-Printer | Where-Object DriverName -eq $name) { Out-Line 'LOG' "Driver conservado (en uso): $name"; return }
    if (-not (Get-PrinterDriver -Name $name -ErrorAction SilentlyContinue)) { return }
    try {
        Remove-PrinterDriver -Name $name -RemoveFromDriverStore -ErrorAction Stop
        Out-Line 'OK' "Driver eliminado: $name"
    } catch {
        try { Remove-PrinterDriver -Name $name -ErrorAction Stop; Out-Line 'OK' "Driver eliminado (paquete en uso): $name" }
        catch { Out-Line 'WARN' "Driver no eliminado ($name): $($_.Exception.Message)" }
    }
}

function Remove-UnusedPort($port) {
    if (-not $port -or (Get-Printer | Where-Object PortName -eq $port)) { return }
    $p = Get-PrinterPort -Name $port -ErrorAction SilentlyContinue
    # solo puertos TCP/IP estandar: nunca USB, WSD, LPT, nul...
    if (-not $p -or -not $p.PrinterHostAddress) { return }
    try { Remove-PrinterPort -Name $port -ErrorAction Stop; Out-Line 'OK' "Puerto eliminado: $port" }
    catch { Out-Line 'WARN' "Puerto no eliminado ($port): $($_.Exception.Message)" }
}

# Comprobacion final: la cola existe, con el driver pedido y sin error
function Test-Queue($name, $driver) {
    $p = Get-Printer -Name $name -ErrorAction SilentlyContinue
    if (-not $p) { throw "la cola no aparece tras crearla" }
    if ($p.DriverName -ne $driver) { throw "driver $($p.DriverName) en lugar de $driver" }
    "$($p.PrinterStatus)"
}

$jobs = @(
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($JobsB64)) -split "`n" | Where-Object { $_.Trim() } | ForEach-Object {
        $f = $_.TrimEnd("`r") -split "`t"
        [pscustomobject]@{
            Kind = $f[0]; Name = $f[1]
            Driver = $(if ($f.Count -gt 2) { $f[2] } else { '' })
            Inf = $(if ($f.Count -gt 3) { $f[3] } else { '' })
            Ip = $(if ($f.Count -gt 4) { $f[4] } else { '' })
        }
    }
)
$errors = 0

try {
    # 0. Cola de impresion en marcha
    $sp = Get-Service Spooler
    if ($sp.Status -ne 'Running') { Start-Service Spooler; Out-Line 'OK' 'Servicio Spooler iniciado' }

    # Cola nueva en una IP que ya tiene cola: se reinstala esa (sin duplicados)
    foreach ($j in $jobs | Where-Object Kind -eq 'N') {
        $port = Get-PrinterPort | Where-Object { $_.PrinterHostAddress -eq $j.Ip } | Select-Object -ExpandProperty Name
        $q = if ($port) { Get-Printer | Where-Object { $port -contains $_.PortName } | Select-Object -First 1 }
        if ($q) { Out-Line 'LOG' "$($j.Ip) ya tiene la cola '$($q.Name)': se reinstala"; $j.Kind = 'E'; $j.Name = $q.Name }
    }

    # 1. Guardar configuracion y quitar colas a reinstalar / eliminar
    Step 5 'Guardando configuración'
    $default = (Get-CimInstance Win32_Printer -Filter 'Default=TRUE' -ErrorAction SilentlyContinue).Name
    $saved = @(); $removed = @()
    foreach ($j in $jobs | Where-Object { $_.Kind -eq 'E' -or $_.Kind -eq 'R' }) {
        $p = Get-Printer -Name $j.Name -ErrorAction SilentlyContinue
        if (-not $p) { Out-Line 'WARN' "No existe la cola '$($j.Name)': se omite"; continue }
        $info = [pscustomobject]@{
            Name = $p.Name; Port = $p.PortName; OldDriver = $p.DriverName; Driver = $j.Driver
            Shared = $p.Shared; ShareName = $p.ShareName; Location = $p.Location; Comment = $p.Comment
            Default = ($p.Name -eq $default); Connection = ($p.Type -eq 'Connection' -or $p.Name -like '\\*')
        }
        if ($j.Kind -eq 'E') { $saved += $info } else { $removed += $info }
        Out-Line 'LOG' "$($j.Kind) $($p.Name) | puerto $($p.PortName) | driver $($p.DriverName)"
    }

    $targets = @($jobs | Where-Object { $_.Kind -ne 'R' -and $_.Driver } | Select-Object Driver, Inf -Unique)

    if ($saved.Count -or $removed.Count) {
        Step 15 'Quitando colas'
        foreach ($s in @($saved) + @($removed)) {
            Get-PrintJob -PrinterName $s.Name -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
            Remove-Printer -Name $s.Name
            if ($s.Connection) {
                # conexion global (printui /ga): quitarla tambien para el resto de usuarios
                Start-Process rundll32.exe -ArgumentList "printui.dll,PrintUIEntry /gd /n`"$($s.Name)`" /q" -Wait -WindowStyle Hidden
            }
            Out-Line 'OK' "Cola quitada: $($s.Name)"
        }

        Step 30 'Reiniciando la cola de impresión'
        Restart-Service Spooler -Force -WarningAction SilentlyContinue

        Step 40 'Limpiando puertos y drivers sin uso'
        foreach ($r in $removed) {
            if (-not $r.Connection) { Remove-UnusedPort $r.Port }
            Out-Line 'SUMMARY' "$($r.Name)  -  quitada (puerto $($r.Port), driver $($r.OldDriver))"
        }
    }
    # drivers antiguos: se limpian al final, cuando las colas ya funcionan con el nuevo (o han vuelto al anterior)
    $oldDrivers = @(@($saved.OldDriver) + @($removed.OldDriver) | Where-Object { $_ -and $targets.Driver -notcontains $_ } | Select-Object -Unique)

    # 2. Drivers: paquete (pnputil + Add-PrinterDriver) o ya instalado / de Windows (Add-PrinterDriver si falta)
    $n = 0
    $badDrivers = @()
    foreach ($t in $targets) {
        $n++
        Step (45 + [int](15 * $n / [Math]::Max(1, $targets.Count))) "Driver $($t.Driver)"
        try {
        if ($t.Inf) {
            # pnputil escribe en la pagina de codigos OEM (850): leerla asi para no romper las tildes
            [Console]::OutputEncoding = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)
            $pnp = & pnputil.exe /add-driver $t.Inf /install 2>&1
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            foreach ($l in $pnp) { if ("$l".Trim()) { Out-Line 'LOG' "$l".Trim() } }
            # con el paquete ya en el almacen, Add-PrinterDriver instala (o actualiza) a su version
            Add-PrinterDriver -Name $t.Driver
        }
        elseif (-not (Get-PrinterDriver -Name $t.Driver -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name $t.Driver }
        $v = [uint64](Get-PrinterDriver -Name $t.Driver | Select-Object -First 1).DriverVersion
        $ver = '{0}.{1}.{2}.{3}' -f (($v -shr 48) -band 0xFFFF), (($v -shr 32) -band 0xFFFF), (($v -shr 16) -band 0xFFFF), ($v -band 0xFFFF)
        Out-Line 'OK' "Driver listo: $($t.Driver) v$ver $(if ($t.Inf) { '(paquete)' } else { '(ya instalado)' })"
        } catch {
            # un driver que falla no tumba el resto: sus colas se saltan (o vuelven a su driver anterior)
            $errors++
            $badDrivers += $t.Driver
            Out-Line 'ERR' "Driver $($t.Driver): $($_.Exception.Message)"
        }
    }

    # 3. Recrear colas existentes
    $snmp = $false
    $i = 0
    foreach ($s in $saved) {
        $i++
        Step (62 + [int](18 * $i / [Math]::Max(1, $saved.Count))) "Reinstalando $($s.Name)"
        if ($badDrivers -contains $s.Driver) {
            if (Get-PrinterDriver -Name $s.OldDriver -ErrorAction SilentlyContinue) {
                Out-Line 'WARN' "$($s.Name): se recrea con su driver anterior ($($s.OldDriver))"
                $s.Driver = $s.OldDriver
            } else {
                $errors++
                Out-Line 'ERR' "$($s.Name): sin driver nuevo ni anterior, no se puede recrear"
                Out-Line 'SUMMARY' "$($s.Name)  -  ERROR: sin driver"
                continue
            }
        }
        try {
            Add-Printer -Name $s.Name -DriverName $s.Driver -PortName $s.Port
            $extra = @{}
            if ($s.Location) { $extra.Location = $s.Location }
            if ($s.Comment)  { $extra.Comment  = $s.Comment }
            if ($s.Shared)   { $extra.Shared = $true; $extra.ShareName = $s.ShareName }
            if ($extra.Count) { Set-Printer -Name $s.Name @extra }
            if ($s.Default) {
                Get-CimInstance Win32_Printer -Filter ("Name='" + ($s.Name -replace "'", "\'") + "'") |
                    Invoke-CimMethod -MethodName SetDefaultPrinter | Out-Null
            }
            if (Enable-PortSnmp $s.Port) { $snmp = $true }
            $st = Test-Queue $s.Name $s.Driver
            Out-Line 'OK' "Reinstalada: $($s.Name) ($($s.Port)) con $($s.Driver) · estado $st"
            Out-Line 'SUMMARY' "$($s.Name)  -  $($s.Driver) en $($s.Port) (antes: $($s.OldDriver))"
        } catch {
            $errors++
            Out-Line 'ERR' "No se pudo recrear $($s.Name): $($_.Exception.Message)"
            Out-Line 'SUMMARY' "$($s.Name)  -  ERROR al recrear (puerto $($s.Port))"
        }
    }

    # 4. Colas nuevas por IP
    foreach ($j in $jobs | Where-Object Kind -eq 'N') {
        Step 85 "Agregando $($j.Name) ($($j.Ip))"
        if ($badDrivers -contains $j.Driver) { Out-Line 'SUMMARY' "$($j.Name)  -  ERROR: driver no disponible"; continue }
        $created = $null
        $portName = $null
        $newPort = -not (Get-PrinterPort | Where-Object { $_.PrinterHostAddress -eq $j.Ip })
        try {
            $portName = Get-IpPort $j.Ip
            $name = $j.Name
            if (Get-Printer -Name $name -ErrorAction SilentlyContinue) { $name = "$name ($($j.Ip))" }
            Add-Printer -Name $name -DriverName $j.Driver -PortName $portName
            $created = $name
            if (Enable-PortSnmp $portName) { $snmp = $true }
            $st = Test-Queue $name $j.Driver
            Out-Line 'OK' "Agregada: $name ($portName) con $($j.Driver) · estado $st"
            Out-Line 'SUMMARY' "$name  -  nueva en $($j.Ip) con $($j.Driver)"
        } catch {
            $errors++
            Out-Line 'ERR' "No se pudo agregar $($j.Name): $($_.Exception.Message)"
            # sin restos a medias: se deshace lo creado
            if ($created) { Remove-Printer -Name $created -ErrorAction SilentlyContinue }
            if ($newPort -and $portName) { Remove-UnusedPort $portName }
            Out-Line 'SUMMARY' "$($j.Name)  -  ERROR al agregar"
        }
    }

    if ($oldDrivers.Count) {
        Step 92 'Limpiando drivers sin uso'
        foreach ($d in $oldDrivers) { Remove-UnusedDriver $d }
    }

    if ($snmp) {
        Step 95 'Aplicando configuración (SNMP para detectar bandejas)'
        Restart-Service Spooler -Force -WarningAction SilentlyContinue
    }
    Step 100 'Finalizado'
} catch {
    $errors++
    Out-Line 'ERR' $_.Exception.Message
}

exit $errors
