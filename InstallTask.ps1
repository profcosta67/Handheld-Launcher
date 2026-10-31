# Cria a Tarefa Agendada no Windows para rodar o Launcher como Admin sem UAC
$exePath = Join-Path $env:LOCALAPPDATA "HandheldLauncher\current\HandheldLauncher.exe"
$action = New-ScheduledTaskAction -Execute $exePath
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -TaskName "HandheldLauncher_Startup" -Description "Inicia o Handheld Launcher automaticamente no logon." -Force