# WebChange Radar

Aplicação para acompanhar alterações no texto de páginas públicas. A primeira etapa contém apenas a API ASP.NET Core em .NET 10, o endpoint de saúde e a estrutura de testes. O produto completo está especificado em [docs/MVP.md](docs/MVP.md).

## Estrutura

- `backend/src/WebChangeRadar.Api`: API e módulos por funcionalidade.
- `backend/tests/WebChangeRadar.UnitTests`: testes unitários para as regras de negócio das próximas etapas.
- `backend/tests/WebChangeRadar.IntegrationTests`: testes HTTP com `WebApplicationFactory`.
- `docs/PROGRESS.md`: progresso da implementação.

## Executar

Instale o SDK do .NET 10 e execute na raiz do repositório:

```powershell
dotnet build --verbosity quiet
dotnet test --verbosity quiet
dotnet run --project backend/src/WebChangeRadar.Api
```

A API escuta em `http://localhost:5010` no perfil padrão. `GET /health` retorna `200 OK` com o texto `Healthy`.

`WebApplicationFactory` inicia a API em memória para testar a rota HTTP sem servidor externo. A configuração de banco de dados, autenticação e verificação de páginas será adicionada nas próximas etapas.
