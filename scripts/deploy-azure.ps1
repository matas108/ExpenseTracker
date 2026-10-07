<#
.SYNOPSIS
  Provisions (first run) and deploys Expense Tracker to Azure App Service (F1 free, Linux)
  with an Azure SQL Database on the free serverless offer. Safe to re-run: existing resources
  are kept, and each run publishes the current code.

.DESCRIPTION
  Passwordless database access: the web app's system-assigned managed identity is added as a
  user in the database, and the SQL server only accepts Microsoft Entra authentication. No SQL
  password is created or stored anywhere.

  Prerequisites: Azure CLI, .NET SDK, and `az login` done beforehand.

.EXAMPLE
  ./scripts/deploy-azure.ps1
  ./scripts/deploy-azure.ps1 -Location northeurope   # if a region rejects new resources, try another
#>
param(
    [string]$ResourceGroup = "rg-expensetracker",
    [string]$Location = "swedencentral",
    # Globally unique names; default derives a stable suffix from the subscription.
    [string]$AppName,
    [string]$SqlServerName,
    [string]$DatabaseName = "expensetracker"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# Both helpers judge success by az's exit code. EAP is relaxed locally because Windows PowerShell
# turns native stderr (az warnings) into terminating errors under "Stop".
function Invoke-Az {
    $ErrorActionPreference = "Continue"
    $out = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Test-Az {
    # Runs an az "show" and reports whether the resource exists, without failing the script.
    $ErrorActionPreference = "Continue"
    & az @args --only-show-errors -o none 2>$null
    return $LASTEXITCODE -eq 0
}

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# --- Context -------------------------------------------------------------------------------
Step "Checking Azure login"
$subscriptionId = Invoke-Az account show --query id -o tsv
$subscriptionName = Invoke-Az account show --query name -o tsv
Write-Host "Subscription: $subscriptionName"

$sha = [Security.Cryptography.SHA256]::Create()
$suffix = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($subscriptionId))) -replace '-', '').Substring(0, 6).ToLower()
if (-not $AppName) { $AppName = "expensetracker-$suffix" }
if (-not $SqlServerName) { $SqlServerName = "sql-expensetracker-$suffix" }
$planName = "plan-expensetracker"

Write-Host "Resource group: $ResourceGroup ($Location)"
Write-Host "Web app:        $AppName"
Write-Host "SQL server:     $SqlServerName / $DatabaseName"

# New subscriptions must register each resource provider once before first use.
Step "Resource providers"
foreach ($ns in "Microsoft.Web", "Microsoft.Sql") {
    if ((Invoke-Az provider show -n $ns --query registrationState -o tsv) -ne "Registered") {
        Write-Host "Registering $ns (one-time, can take a minute)..."
        Invoke-Az provider register -n $ns --wait -o none
    }
}

# --- Resource group + App Service -----------------------------------------------------------
Step "Resource group"
# The group only holds metadata; resources go to $Location even if the group lives elsewhere.
if (-not (Test-Az group show -n $ResourceGroup)) {
    Invoke-Az group create -n $ResourceGroup -l $Location -o none
}

Step "App Service plan (F1 free, Linux)"
if (-not (Test-Az appservice plan show -g $ResourceGroup -n $planName)) {
    Invoke-Az appservice plan create -g $ResourceGroup -n $planName --sku F1 --is-linux -o none
}

Step "Web app"
if (-not (Test-Az webapp show -g $ResourceGroup -n $AppName)) {
    $runtime = Invoke-Az webapp list-runtimes --os linux --query "[?config=='DOTNETCORE|10.0'].config | [0]" -o tsv
    if (-not $runtime) { throw "No .NET 10 runtime offered on App Service Linux in this CLI/region." }
    $runtime = $runtime.Replace('|', ':')   # az.cmd would treat a raw "|" as a pipe
    Invoke-Az webapp create -g $ResourceGroup -p $planName -n $AppName --runtime $runtime -o none
}
Invoke-Az webapp update -g $ResourceGroup -n $AppName --https-only true -o none
$appPrincipalId = Invoke-Az webapp identity assign -g $ResourceGroup -n $AppName --query principalId -o tsv

# --- Azure SQL (Entra-only auth, free offer) -------------------------------------------------
Step "SQL server (Microsoft Entra authentication only)"
if (-not (Test-Az sql server show -g $ResourceGroup -n $SqlServerName)) {
    $me = Invoke-Az ad signed-in-user show --query "{upn:userPrincipalName, id:id}" -o json | ConvertFrom-Json
    Invoke-Az sql server create -g $ResourceGroup -n $SqlServerName -l $Location `
        --enable-ad-only-auth --external-admin-principal-type User `
        --external-admin-name $me.upn --external-admin-sid $me.id -o none
}

# 0.0.0.0 is Azure's special rule for "allow Azure services", which covers the web app.
if (-not (Test-Az sql server firewall-rule show -g $ResourceGroup -s $SqlServerName -n AllowAzureServices)) {
    Invoke-Az sql server firewall-rule create -g $ResourceGroup -s $SqlServerName -n AllowAzureServices `
        --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none
}

Step "SQL database (free serverless offer, auto-pause when the monthly free limit is used)"
if (-not (Test-Az sql db show -g $ResourceGroup -s $SqlServerName -n $DatabaseName)) {
    Invoke-Az sql db create -g $ResourceGroup -s $SqlServerName -n $DatabaseName `
        --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 2 `
        --use-free-limit --free-limit-exhaustion-behavior AutoPause -o none
}

# --- Grant the web app's identity access to the database -------------------------------------
Step "Granting the web app's managed identity access to the database"
$sqlHost = "$SqlServerName.database.windows.net"
$myIp = (Invoke-RestMethod -Uri "https://api.ipify.org").Trim()
Invoke-Az sql server firewall-rule create -g $ResourceGroup -s $SqlServerName -n DeployClient `
    --start-ip-address $myIp --end-ip-address $myIp -o none
try {
    $token = Invoke-Az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    $conn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$sqlHost,1433;Database=$DatabaseName;Encrypt=True;Connect Timeout=90")
    $conn.AccessToken = $token
    # Serverless DB may be resuming from pause; retry the first connection.
    for ($i = 1; ; $i++) {
        try { $conn.Open(); break }
        catch { if ($i -ge 5) { throw }; Write-Host "  database waking up, retrying ($i)..."; Start-Sleep 15 }
    }
    $cmd = $conn.CreateCommand()
    # db_owner because the app applies EF Core migrations on startup.
    $cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$AppName')
    CREATE USER [$AppName] FROM EXTERNAL PROVIDER;
IF IS_ROLEMEMBER('db_owner', N'$AppName') = 0
    ALTER ROLE db_owner ADD MEMBER [$AppName];
"@
    [void]$cmd.ExecuteNonQuery()
    $conn.Close()
}
finally {
    Invoke-Az sql server firewall-rule delete -g $ResourceGroup -s $SqlServerName -n DeployClient -o none
}

# --- App configuration -----------------------------------------------------------------------
Step "App settings"
$connectionString = "Server=tcp:$sqlHost,1433;Database=$DatabaseName;Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60"
Invoke-Az webapp config connection-string set -g $ResourceGroup -n $AppName --connection-string-type SQLAzure `
    --settings "Default=$connectionString" -o none

$existingKey = Invoke-Az webapp config appsettings list -g $ResourceGroup -n $AppName --query "[?name=='Jwt__Key'].value | [0]" -o tsv
# ForwardedHeaders: App Service sits behind a proxy; this makes the app see the client's real IP
# (from X-Forwarded-For), which the rate limiter partitions on.
$settings = @("Database__Provider=SqlServer", "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true")
if (-not $existingKey) {
    # Generated once and kept on re-runs so issued tokens stay valid. Never printed.
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $settings += "Jwt__Key=$([Convert]::ToBase64String($bytes))"
}
Invoke-Az webapp config appsettings set -g $ResourceGroup -n $AppName --settings @settings -o none
Invoke-Az webapp config set -g $ResourceGroup -n $AppName --startup-file "dotnet ExpenseTracker.Api.dll" -o none

# --- Build + deploy ---------------------------------------------------------------------------
Step "Publishing"
$publishDir = Join-Path $env:TEMP "expensetracker-publish"
$zip = Join-Path $env:TEMP "expensetracker.zip"
Remove-Item $publishDir, $zip -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root "src/ExpenseTracker.Api") -c Release -r linux-x64 --self-contained false -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
# Not Compress-Archive: Windows PowerShell writes "\" into entry names, which Linux treats as
# part of the file name. Zip entries must use "/".
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    $base = (Resolve-Path $publishDir).Path.TrimEnd('\') + '\'
    Get-ChildItem $publishDir -Recurse -File | ForEach-Object {
        $entryName = $_.FullName.Substring($base.Length).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName)
    }
}
finally { $archive.Dispose() }

Step "Deploying"
$ErrorActionPreference = "Continue"
& az webapp deploy -g $ResourceGroup -n $AppName --src-path $zip --type zip --clean true -o none
$deployExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($deployExit -ne 0) {
    # On Linux, the deployment service (Kudu) restarts together with the app at the end of a deploy,
    # so the CLI's status polling can get a 502 even though the deployment finished. The server's
    # own deployment log is the source of truth.
    $lastLogLine = Invoke-Az webapp log deployment show -g $ResourceGroup -n $AppName --query "[-1].message" -o tsv
    if ($lastLogLine -notmatch "Deployment successful") {
        throw "Deployment failed. Last deployment log line: $lastLogLine"
    }
    Write-Host "The CLI lost contact while the app restarted, but the deployment log reports success." -ForegroundColor Yellow
}

$url = "https://$AppName.azurewebsites.net"
Write-Host "`nDeployed: $url  (Swagger: $url/swagger)" -ForegroundColor Green
Write-Host "First request can take ~1 min: F1 cold start + the database resuming from auto-pause."
