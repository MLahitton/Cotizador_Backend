# PostgreSQL integration tests

The relational tests create a uniquely named temporary database, apply the
real EF Core migrations, and remove that database after the xUnit collection
finishes. They never read `appsettings.json`.

Set an administrative test connection in PowerShell without storing secrets:

```powershell
$env:COTIZADOR_TEST_POSTGRES_ADMIN_CONNECTION_STRING = "Host=127.0.0.1;Port=5432;Database=postgres;Username=USUARIO;Password=CLAVE"
dotnet test CotizadorBackend.sln --filter "Category=PostgreSql"
```

The administrative database must be exactly `postgres`. Temporary databases
must start with `cotizador_backend_test_`; creation, connection termination,
and deletion are restricted to that prefix. If the variable is absent, the
relational tests are explicitly skipped and no fallback connection is used.
