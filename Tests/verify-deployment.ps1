<#
.SYNOPSIS
  Checks a BillerRestoApi log file (api-YYYYMMDD.log) against the post-deployment criteria of the login-performance release.
  Read-only: it only reads the log file you give it. It never contacts the server and prints no secrets (the log has none).

.EXAMPLE
  .\verify-deployment.ps1 -LogPath C:\Downloads\api-20261017.log.txt
#>
param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [string]$ExpectedBuild = 'e68c18e78f90'
)

$rows = Get-Content $LogPath | Where-Object { $_.Trim().StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json }
$has = { param($o, $n) $o.PSObject.Properties.Name -contains $n }
$fail = 0
function Result($ok, $text) { if ($ok) { "PASS  $text" } else { $script:fail++; "FAIL  $text" } }

# Look only at the newest process start that reports the expected build (or the newest start if none does).
$starts = @($rows | Where-Object { $_.evt -eq 'app.start' })
$mine   = @($starts | Where-Object { (& $has $_ 'build') -and $_.build -eq $ExpectedBuild })
"app.start lines: $($starts.Count); with build ${ExpectedBuild}: $($mine.Count)"
Result ($mine.Count -ge 1) "app.start reports build $ExpectedBuild"
$startTs = if ($mine.Count) { [datetime]::Parse($mine[-1].ts) } else { [datetime]::MinValue }

$warm = @($rows | Where-Object { $_.evt -eq 'app.warmup' -and [datetime]::Parse($_.ts) -ge $startTs })
Result ($warm.Count -ge 1 -and $warm[-1].pbkdf2 -eq 'native-cng') "app.warmup reports pbkdf2 = native-cng$(if ($warm.Count) { ' (found: ' + $warm[-1].pbkdf2 + ')' } else { ' (no app.warmup line)' })"
Result ($warm.Count -ge 1 -and $warm[-1].db -eq 'ok') "app.warmup reports database warm-up = ok$(if ($warm.Count) { ' (found: ' + $warm[-1].db + ')' })"
if ($mine.Count) {
    "app.start flags: requireHttps=$($mine[-1].requireHttps) trustForwardedProto=$($mine[-1].trustForwardedProto) (HTTPS must stay unchanged: requireHttps=false)"
    Result ($mine[-1].requireHttps -eq $false) "HTTPS enforcement unchanged (requireHttps=false)"
    Result ($mine[-1].signingKey -like 'ok-*') "signing key configured (status: $($mine[-1].signingKey))"
}

$logins = @($rows | Where-Object { $_.evt -eq 'login.stages' -and (& $has $_ 'stageMs') -and [datetime]::Parse($_.ts) -ge $startTs })
$http   = @{}; $rows | Where-Object { $_.evt -eq 'http.request' -and $_.route -eq '/api/auth/login' } | ForEach-Object { $http[$_.cid] = $_ }

"`nLogins with timings since the deployment start: $($logins.Count) (need at least 5)"
Result ($logins.Count -ge 5) "at least 5 login.stages lines carry stageMs"

$table = $logins | ForEach-Object {
    $h = $http[$_.cid]
    [pscustomobject]@{
        Time        = ([datetime]::Parse($_.ts)).ToUniversalTime().ToString('HH:mm:ss')
        LoginNo     = $_.loginNo
        SinceStartS = [math]::Round($_.sinceStartMs / 1000, 1)
        Outcome     = $_.outcome
        HttpMs      = if ($h) { $h.durationMs } else { $null }
        TotalMs     = $_.totalMs
        Pbkdf2      = $_.pbkdf2
        Scheme      = $_.scheme
        Xfp         = $_.xfp
        StageMs     = $_.stageMs
    }
}
$table | Format-Table -AutoSize -Wrap | Out-String -Width 250

if ($logins.Count) {
    Result (@($logins | Where-Object { $_.pbkdf2 -ne 'native-cng' }).Count -eq 0) "every login ran with pbkdf2 = native-cng"
    $second = @($table | Where-Object { $_.LoginNo -gt 1 -and $_.HttpMs })
    if ($second.Count) {
        $avg = [int](($second | Measure-Object HttpMs -Average).Average)
        "Warm logins (loginNo > 1): n=$($second.Count) avg http durationMs = $avg ms   [baseline before the change: 1,373-1,694 ms]"
    }
    $first = @($table | Where-Object { $_.LoginNo -eq 1 -and $_.HttpMs })
    if ($first.Count) { "First login after start: $($first[0].HttpMs) ms   [baseline before the change: 3,252-4,797 ms]" }
    "Observed (not a pass/fail criterion): improvement is only claimed from these measured numbers."
}

"`nResult: $(if ($fail -eq 0) { 'ALL CHECKS PASSED' } else { "$fail CHECK(S) FAILED" })"
exit $fail
