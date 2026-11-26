# Выполняется НА удалённом ПК (от SYSTEM через PsExec).
# 1) закрывает 7-Zip, 2) удаляет все установленные версии (EXE и MSI), 3) ставит C:\Windows\Temp\7-zip.msi
$ErrorActionPreference = 'Continue'
$msi = 'C:\Windows\Temp\7-zip.msi'
$log = 'C:\Windows\Temp\7zip-install.log'

if (-not (Test-Path $msi)) { Write-Error "Нет файла $msi"; exit 10 }

Get-Process 7zFM, 7zG -ErrorAction SilentlyContinue | Stop-Process -Force

# --- удаление всех версий 7-Zip (64- и 32-битные записи) ---
$keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
$old = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
         Where-Object { $_.DisplayName -like '7-Zip*' })

if ($old.Count -eq 0) { Write-Host 'Старых версий 7-Zip не найдено' }

foreach ($app in $old) {
    Write-Host "Удаляю: $($app.DisplayName)"
    if ($app.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') {
        # установлен как MSI
        $p = Start-Process msiexec.exe -ArgumentList "/x $($app.PSChildName) /qn /norestart" -Wait -PassThru
        Write-Host "  msiexec /x -> код $($p.ExitCode)"
    }
    else {
        # установлен как EXE
        $exe = ($app.UninstallString -replace '"', '').Trim()
        if (Test-Path $exe) {
            Start-Process $exe -ArgumentList '/S' -Wait
            Start-Sleep -Seconds 3   # деинсталлятор может вернуть управление раньше, чем закончит
            Write-Host "  Uninstall.exe /S выполнен"
        }
        else { Write-Warning "  Не найден деинсталлятор: $exe" }
    }
}

# --- установка новой версии ---
$p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$log`"" -Wait -PassThru
Write-Host "Установка MSI -> код $($p.ExitCode)"

$exe7z = @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") |
         Where-Object { Test-Path $_ } | Select-Object -First 1
if ($exe7z) { Write-Host "Установлена версия: $((Get-Item $exe7z).VersionInfo.ProductVersion)" }

# 0 = успех, 3010 = успех, но нужна перезагрузка
if ($p.ExitCode -in 0, 3010) { exit 0 } else { exit $p.ExitCode }
