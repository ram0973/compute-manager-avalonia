# Выполняется НА удалённом ПК от SYSTEM. Вшит в exe программы и доставляется оттуда.
# Порядок: проверить хэш установщика -> закрыть процессы -> удалить старые версии -> установить.
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Sha256,
    [string]$Arguments = '',
    [string]$UninstallPattern = '',
    [string]$UninstallArguments = '/S',
    [string]$Kill = ''
)

$ErrorActionPreference = 'Continue'
$log = 'C:\Windows\Temp\pkg-install.log'

if (-not (Test-Path -LiteralPath $Installer)) { Write-Error "Нет файла $Installer"; exit 10 }

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Installer).Hash
if ($actual -ne $Sha256) {
    Write-Error "Хэш установщика не совпадает (ожидался $Sha256, получен $actual)"
    Remove-Item -LiteralPath $Installer -Force -ErrorAction SilentlyContinue
    exit 11
}
Write-Host "Хэш установщика совпал: $actual"

foreach ($name in ($Kill -split ',' | Where-Object { $_ })) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
}

if ($UninstallPattern) {
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    $old = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
             Where-Object { $_.DisplayName -like $UninstallPattern })

    if ($old.Count -eq 0) { Write-Host 'Старых версий не найдено' }

    foreach ($app in $old) {
        Write-Host "Удаляю: $($app.DisplayName)"
        if ($app.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') {
            $u = Start-Process msiexec.exe -ArgumentList "/x $($app.PSChildName) /qn /norestart" -Wait -PassThru
            Write-Host "  msiexec /x -> код $($u.ExitCode)"
        }
        else {
            $exe = ($app.UninstallString -replace '"', '').Trim()
            if (Test-Path -LiteralPath $exe) {
                Start-Process -FilePath $exe -ArgumentList $UninstallArguments -Wait
                Start-Sleep -Seconds 3
                Write-Host '  деинсталлятор выполнен'
            }
            else { Write-Warning "  Не найден деинсталлятор: $exe" }
        }
    }
}

if ([IO.Path]::GetExtension($Installer).ToLowerInvariant() -eq '.msi') {
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$Installer`" /qn /norestart $Arguments /l*v `"$log`"" -Wait -PassThru
}
elseif ($Arguments) {
    $p = Start-Process -FilePath $Installer -ArgumentList $Arguments -Wait -PassThru
}
else {
    $p = Start-Process -FilePath $Installer -Wait -PassThru
}
Write-Host "Установка -> код $($p.ExitCode)"

Remove-Item -LiteralPath $Installer -Force -ErrorAction SilentlyContinue
if ($p.ExitCode -in 0, 3010) { exit 0 } else { exit $p.ExitCode }
