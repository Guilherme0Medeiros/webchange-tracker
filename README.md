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
dotnet run --project backend/src/WebChangeRadar.Api --launch-profile https
```

A API escuta em `https://localhost:7010` no perfil HTTPS. Para confiar no certificado local, execute `dotnet dev-certs https --trust`. `GET /health` retorna `200 OK` com o texto `Healthy`. Em desenvolvimento, o documento OpenAPI fica em `/openapi/v1.json`.

`ApplicationDbContext` é o contexto do EF Core: ele mapeia usuários do Identity e monitoramentos para o PostgreSQL. A migration versionada cria as tabelas e as restrições. A API lê a conexão da variável de ambiente `ConnectionStrings__DefaultConnection`; não armazene a senha em arquivos versionados.

Para executar os testes com PostgreSQL real, defina `WEBCHANGE_TEST_CONNECTION` com uma conexão administrativa a um banco de teste (por exemplo, `Database=postgres`). Os testes criam e removem um banco isolado. Sem essa variável, os testes que exigem PostgreSQL são ignorados. O CI fornece um contêiner temporário para executá-los.

## Autenticação

As rotas ficam sob `/api/auth`: `POST /register`, `POST /login`, `POST /logout` e `GET /me`. Cadastro cria a conta e inicia a sessão. `/me` e `/logout` exigem autenticação. Identity gerencia hashes de senha e bloqueio temporário após tentativas incorretas. Login e cadastro têm limite de dez requisições por minuto por IP.

A interface futura deve acessar a API por **mesma origem**, via proxy, usando HTTPS. Antes de cada `POST`, faça `GET /api/auth/csrf` e envie o `requestToken` recebido no cabeçalho `X-CSRF-TOKEN`. Após cadastro, login ou logout, obtenha um novo token. Os cookies de sessão e CSRF são `Secure`, `HttpOnly` e `SameSite=Strict`; o JavaScript não precisa lê-los. O navegador envia os cookies automaticamente nas requisições da mesma origem.

Para uma implantação com várias instâncias, será necessário compartilhar as chaves de Data Protection que assinam os cookies. A configuração de produção ainda não faz parte desta etapa.
