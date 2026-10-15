# WebChange Radar

Aplicação para acompanhar alterações no texto de páginas públicas. O back-end usa ASP.NET Core em .NET 10, PostgreSQL e Entity Framework Core. O produto completo está especificado em [docs/MVP.md](docs/MVP.md).

## Estrutura

- `backend/src/WebChangeRadar.Api`: API e módulos por funcionalidade.
- `backend/tests/WebChangeRadar.UnitTests`: testes unitários para as regras de negócio das próximas etapas.
- `backend/tests/WebChangeRadar.IntegrationTests`: testes HTTP com `WebApplicationFactory`.
- `docs/PROGRESS.md`: progresso da implementação.

## Executar

Instale o SDK do .NET 10 e Docker. Copie `.env.example` para `.env`, escolha uma senha local e execute na raiz do repositório:

```powershell
docker compose up -d
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=webchange_radar;Username=webchange;Password=<senha-do-.env>"
dotnet tool restore
dotnet ef database update --project backend/src/WebChangeRadar.Api --startup-project backend/src/WebChangeRadar.Api
dotnet build --verbosity quiet
dotnet test --verbosity quiet
dotnet run --project backend/src/WebChangeRadar.Api
```

A API escuta em `http://localhost:5010` no perfil padrão. `GET /health` retorna `200 OK` com o texto `Healthy`.

`ApplicationDbContext` é o contexto do EF Core: ele mapeia usuários do Identity e monitoramentos para o PostgreSQL. A migration versionada cria as tabelas e as restrições. A API lê a conexão da variável de ambiente `ConnectionStrings__DefaultConnection`; não armazene a senha em arquivos versionados.

Para executar os testes com PostgreSQL real, defina `WEBCHANGE_TEST_CONNECTION` com uma conexão administrativa a um banco de teste (por exemplo, `Database=postgres`). Os testes criam e removem um banco isolado. Sem essa variável, os testes que exigem PostgreSQL são ignorados. O CI fornece um contêiner temporário para executá-los.
