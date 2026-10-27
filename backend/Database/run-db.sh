#!/usr/bin/env bash
# Creates InPatientDb and runs 001_schema.sql, 002_seed.sql, 003_views.sql in order.
# Usage:
#   ./run-db.sh                                   # LocalDB is Windows-only; on macOS/Linux use Docker SQL Server:
#   SERVER="localhost,1433" SQLUSER=sa SQLPASSWORD='<pwd>' ./run-db.sh --reset
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SERVER="${SERVER:-(localdb)\\MSSQLLocalDB}"
DATABASE="${DATABASE:-InPatientDb}"
RESET=0
[[ "${1:-}" == "--reset" ]] && RESET=1

if [[ -n "${SQLUSER:-}" ]]; then AUTH=(-U "$SQLUSER" -P "$SQLPASSWORD"); else AUTH=(-E); fi
run() { sqlcmd -S "$SERVER" "${AUTH[@]}" -b -C -I "$@"; }

if [[ $RESET -eq 1 ]]; then
  echo "Dropping $DATABASE (if it exists)..."
  run -d master -Q "IF DB_ID(N'$DATABASE') IS NOT NULL BEGIN ALTER DATABASE [$DATABASE] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DATABASE]; END"
fi
run -d master -Q "IF DB_ID(N'$DATABASE') IS NULL CREATE DATABASE [$DATABASE];"
for f in 001_schema.sql 002_seed.sql 003_views.sql; do
  echo "Running $f..."
  run -d "$DATABASE" -i "$HERE/$f"
done
echo "Database ready."
