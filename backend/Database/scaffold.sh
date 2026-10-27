#!/usr/bin/env bash
# Re-generates EF Core entities + DbContext from the database (database-first).
# Never edit generated files; customise in InPatient.Api/Data/Partials/*.cs.
set -euo pipefail
CONN="${1:-Server=(localdb)\\MSSQLLocalDB;Database=InPatientDb;Trusted_Connection=True;TrustServerCertificate=True}"
cd "$(dirname "$0")/../InPatient.Api"
dotnet ef dbcontext scaffold "$CONN" Microsoft.EntityFrameworkCore.SqlServer \
  -o Data/Entities --context-dir Data --context InPatientDbContext --no-onconfiguring --force
