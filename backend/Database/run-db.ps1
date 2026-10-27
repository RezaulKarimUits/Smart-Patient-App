<#
  Creates the InPatientDb database and runs 001_schema.sql, 002_seed.sql, 003_views.sql in order.
  Usage:
    ./run-db.ps1                      # create if missing, then run scripts (all idempotent)
    ./run-db.ps1 -Reset               # drop + recreate (fresh demo data relative to now)
    ./run-db.ps1 -Server ".\SQLEXPRESS"
    ./run-db.ps1 -Server "localhost,1433" -User sa -Password "<pwd>"   # Docker / SQL auth
  Requires sqlcmd (ships with SQL Server / LocalDB tools, or `winget install Microsoft.Sqlcmd`).
#>
param(
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [string]$Database = "InPatientDb",
    [string]$User,
    [string]$Password,
    [switch]$Reset
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$auth = @("-E")
if ($User) { $auth = @("-U", $User, "-P", $Password) }
$common = @("-S", $Server) + $auth + @("-b", "-C", "-I")

function Invoke-Sql([string[]]$extra) {
    & sqlcmd @common @extra
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed ($LASTEXITCODE): $extra" }
}

if ($Reset) {
    Write-Host "Dropping $Database (if it exists)..." -ForegroundColor Yellow
    Invoke-Sql @("-d", "master", "-Q", "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END")
}

Write-Host "Ensuring database $Database exists on $Server..." -ForegroundColor Cyan
Invoke-Sql @("-d", "master", "-Q", "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];")

foreach ($f in "001_schema.sql", "002_seed.sql", "003_views.sql") {
    Write-Host "Running $f..." -ForegroundColor Cyan
    Invoke-Sql @("-d", $Database, "-i", (Join-Path $here $f))
}
Write-Host "Database ready." -ForegroundColor Green
