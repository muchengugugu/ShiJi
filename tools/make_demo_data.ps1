param(
    [string]$DataDir = "$PSScriptRoot\..\.demodata"
)

$ErrorActionPreference = 'Stop'
Remove-Item $DataDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

function ISO([datetime]$local) {
    $offset = [TimeZoneInfo]::Local.GetUtcOffset($local)
    return ([DateTimeOffset]::new($local, $offset)).ToUniversalTime().ToString('o')
}

$apps = @(
    @{ id='app_chrome';  name='Google Chrome';    executablePath='C:\Program Files\Google\Chrome\Application\chrome.exe'; processName='chrome';   color='#4F7CFF'; enabled=$true; addedAt=(ISO((Get-Date).Date.AddDays(-45))) },
    @{ id='app_word';    name='Microsoft Word';   executablePath='C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE'; processName='WINWORD'; color='#2B579A'; enabled=$true; addedAt=(ISO((Get-Date).Date.AddDays(-45))) },
    @{ id='app_notepad'; name='Notepad';          executablePath='C:\Windows\System32\notepad.exe'; processName='notepad'; color='#18B7A0'; enabled=$true; addedAt=(ISO((Get-Date).Date.AddDays(-44))) },
    @{ id='app_calc';    name='Calculator';       executablePath='C:\Windows\System32\calc.exe'; processName='CalculatorApp'; color='#FF9F43'; enabled=$true; addedAt=(ISO((Get-Date).Date.AddDays(-40))) },
    @{ id='app_explorer';name='File Explorer';    executablePath='C:\Windows\explorer.exe'; processName='explorer'; color='#F7B731'; enabled=$true; addedAt=(ISO((Get-Date).Date.AddDays(-39))) }
)

$appMap = @{}
foreach ($a in $apps) { $appMap[$a.id] = $a }

$sessions = New-Object System.Collections.Generic.List[object]
$script:idx = 0
function AddSession([string]$appId, [datetime]$startLocal, [datetime]$endLocal, [int]$peak = 1) {
    $app = $appMap[$appId]
    $endIso = ISO $endLocal
    $script:idx++
    $sessions.Add([ordered]@{
        id = ('sess_{0:d4}' -f $script:idx)
        appId = $appId
        appName = $app.name
        appColor = $app.color
        start = (ISO $startLocal)
        end = $endIso
        lastSeen = $endIso
        peakInstances = $peak
    })
}

$today = (Get-Date).Date
# Today: Chrome, Notepad and Explorer started; Word and Calculator have not.
AddSession 'app_chrome'  $today.AddHours(8).AddMinutes(55)  $today.AddHours(12).AddMinutes(10) 2
AddSession 'app_chrome'  $today.AddHours(13).AddMinutes(20) $today.AddHours(14).AddMinutes(5)  1
AddSession 'app_chrome'  $today.AddHours(15).AddMinutes(40) $today.AddHours(17).AddMinutes(25) 2
AddSession 'app_notepad' $today.AddHours(10).AddMinutes(5)  $today.AddHours(10).AddMinutes(22)
AddSession 'app_notepad' $today.AddHours(17).AddMinutes(30) $today.AddHours(17).AddMinutes(42)
AddSession 'app_explorer' $today.AddHours(16).AddMinutes(2) $today.AddHours(16).AddMinutes(11)

$random = [System.Random]::new(20261023)
for ($offset = 1; $offset -le 29; $offset++) {
    $day = $today.AddDays(-$offset)
    $weekend = $day.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday)

    if ($weekend) {
        AddSession 'app_chrome' $day.AddHours(10).AddMinutes(30) $day.AddHours(12).AddMinutes(8) 2
        if ($random.NextDouble() -gt 0.35) {
            AddSession 'app_chrome' $day.AddHours(20).AddMinutes(10) $day.AddHours(21).AddMinutes(55) 1
        }
    } else {
        AddSession 'app_chrome' $day.AddHours(9).AddMinutes(2) $day.AddHours(11).AddMinutes(23) 2
        if ($random.NextDouble() -gt 0.22) {
            AddSession 'app_chrome' $day.AddHours(13).AddMinutes(35) $day.AddHours(16).AddMinutes(12) 3
        }
    }

    if ($day.DayOfWeek -in @([DayOfWeek]::Monday, [DayOfWeek]::Wednesday, [DayOfWeek]::Friday)) {
        AddSession 'app_word' $day.AddHours(10).AddMinutes(18) $day.AddHours(11).AddMinutes(52) 1
        if ($random.NextDouble() -gt 0.65) {
            AddSession 'app_word' $day.AddHours(15).AddMinutes(5) $day.AddHours(16).AddMinutes(20) 1
        }
    } elseif ($random.NextDouble() -gt 0.72) {
        AddSession 'app_word' $day.AddHours(14).AddMinutes(10) $day.AddHours(15).AddMinutes(2) 1
    }

    if ($random.NextDouble() -gt 0.38) {
        AddSession 'app_notepad' $day.AddHours(15).AddMinutes(2) $day.AddHours(15).AddMinutes(17 + $random.Next(0, 22)) 1
    }
    if ($random.NextDouble() -gt 0.70) {
        AddSession 'app_calc' $day.AddHours(16).AddMinutes(28) $day.AddHours(16).AddMinutes(36 + $random.Next(0, 10)) 1
    }
    if ($random.NextDouble() -gt 0.58) {
        AddSession 'app_explorer' $day.AddHours(18).AddMinutes(1) $day.AddHours(18).AddMinutes(8 + $random.Next(0, 9)) 1
    }
}

$data = [ordered]@{
    version = 1
    apps = $apps
    sessions = $sessions
    settings = [ordered]@{
        launchAtStartup = $false
        minimizeToTray = $true
        firstTrayHintShown = $true
        lastView = 'overview'
    }
    updatedAt = (Get-Date).ToUniversalTime().ToString('o')
}

$json = $data | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText((Join-Path $DataDir 'data.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Wrote $($sessions.Count) sessions for $($apps.Count) apps to $DataDir"
