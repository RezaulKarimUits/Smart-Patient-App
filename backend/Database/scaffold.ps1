<#
  Re-generates EF Core entities + DbContext from the database (database-first).
  Never edit the generated files; customise in InPatient.Api/Data/Partials/*.cs.
  Usage: ./scaffold.ps1 [-Connection "<conn string>"]
#>
param(
    [string]$Connection = "Server=(localdb)\MSSQLLocalDB;Database=InPatientDb;Trusted_Connection=True;TrustServerCertificate=True"
)
$ErrorActionPreference = "Stop"
$api = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\InPatient.Api"
Push-Location $api
try {
    dotnet ef dbcontext scaffold $Connection Microsoft.EntityFrameworkCore.SqlServer `
        -o Data/Entities --context-dir Data --context InPatientDbContext --no-onconfiguring --force
    if ($LASTEXITCODE -ne 0) { throw "scaffold failed" }
} finally { Pop-Location }
