# WebChange Radar

Aplicação para acompanhar alterações no texto de páginas públicas. O back-end usa ASP.NET Core em .NET 10, PostgreSQL e Entity Framework Core. O produto completo está especificado em [docs/MVP.md](docs/MVP.md).

## Estrutura

- `backend/src/WebChangeRadar.Api`: API e módulos por funcionalidade.
- `backend/tests/WebChangeRadar.UnitTests`: testes unitários para as regras de negócio das próximas etapas.
- `backend/tests/WebChangeRadar.IntegrationTests`: testes HTTP com `WebApplicationFactory`.
- `docs/PROGRESS.md`: progresso da implementação.

## Executar

Instale o SDK do .NET 10 e PostgreSQL local. Copie `.env.example` para `.env` e configure as conexões de `webchange_dev` e `webchange_test` para o usuário `webchange_app`. A API carrega esse arquivo automaticamente apenas em `Development`; variáveis de ambiente definidas no processo têm prioridade. Em produção, use variáveis de ambiente normais. Execute na raiz do repositório:

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/WebChangeRadar.Api --startup-project backend/src/WebChangeRadar.Api
dotnet build --verbosity quiet
dotnet test --verbosity quiet
dotnet run --project backend/src/WebChangeRadar.Api --launch-profile https
```

A API escuta em `https://localhost:7010` no perfil HTTPS. Para confiar no certificado local, execute `dotnet dev-certs https --trust`. `GET /health` retorna `200 OK` com o texto `Healthy`. Em desenvolvimento, o documento OpenAPI fica em `/openapi/v1.json`.

Como alternativa ao PostgreSQL instalado no Windows, `docker compose up -d` inicia o PostgreSQL em contêiner e cria os dois bancos na primeira inicialização do volume. Escolha uma porta livre em `POSTGRES_PORT` se já houver um servidor local em 5432. Um volume antigo não executa novamente o script de criação do banco de testes.

`ApplicationDbContext` é o contexto do EF Core: ele mapeia usuários do Identity e monitoramentos para o PostgreSQL. As migrations versionadas criam as tabelas e as restrições. Não armazene credenciais em arquivos versionados.

Os testes PostgreSQL usam `WEBCHANGE_TEST_CONNECTION`, carregada do ambiente ou do `.env`. O usuário precisa poder criar esquemas em `webchange_test`; cada fixture cria e remove seu próprio esquema. A conexão de teste deve apontar para um banco diferente da conexão de desenvolvimento. Sem essa configuração, os testes que exigem PostgreSQL são ignorados. O CI fornece um contêiner temporário.

## Autenticação

As rotas ficam sob `/api/auth`: `POST /register`, `POST /login`, `POST /logout` e `GET /me`. Cadastro cria a conta e inicia a sessão. `/me` e `/logout` exigem autenticação. Identity gerencia hashes de senha e bloqueio temporário após tentativas incorretas. Login e cadastro têm limite de dez requisições por minuto por IP.

A interface futura deve acessar a API por **mesma origem**, via proxy, usando HTTPS. Antes de cada `POST`, faça `GET /api/auth/csrf` e envie o `requestToken` recebido no cabeçalho `X-CSRF-TOKEN`. Após cadastro, login ou logout, obtenha um novo token. Os cookies de sessão e CSRF são `Secure`, `HttpOnly` e `SameSite=Strict`; o JavaScript não precisa lê-los. O navegador envia os cookies automaticamente nas requisições da mesma origem.

Para uma implantação com várias instâncias, será necessário compartilhar as chaves de Data Protection que assinam os cookies. A configuração de produção ainda não faz parte desta etapa.

## Monitoramentos

As rotas autenticadas em `/api/monitors` permitem criar, listar, consultar, editar e excluir monitoramentos. `PATCH /api/monitors/{id}/status` recebe `{"status":"paused"}` ou `{"status":"active"}`. Criação e edição recebem `name`, `url` e `intervalMinutes` (60, 360 ou 1440). Há no máximo três monitoramentos por usuário. Escritas exigem o cabeçalho CSRF descrito acima.

A listagem aceita `status=active|paused`, `page` (a partir de 1) e `pageSize` (1 a 100, padrão 20). Retorna `items`, `page`, `pageSize` e `totalCount`. O monitor novo começa ativo e disponível para a primeira verificação imediatamente. Alterar a URL marca a necessidade de uma nova linha de base e antecipa a próxima verificação; alterar somente o intervalo a agenda a partir da atualização. Ao pausar, `nextCheckAt` fica nulo; reativar agenda uma verificação imediata.

Os contratos HTTP usam DTOs (`MonitorRequest` e `MonitorResponse`) para não expor diretamente a entidade do EF Core. `TimeProvider` fornece o horário UTC para agendamento e facilita testes determinísticos. Uma transação com bloqueio da linha do usuário mantém o limite de três monitores mesmo com criações simultâneas.

## Verificações, histórico e notificações

Configure `InternalJobs__Token` com um segredo aleatório e agende uma chamada periódica a `POST /internal/jobs/process-due` com `Authorization: Bearer <segredo>`. O endpoint cria trabalhos para monitores vencidos e processa até dez verificações por chamada. Use um agendador externo; a API não exige um processo residente para disparar o trabalho. O banco guarda trabalhos pendentes, leases temporárias e resultados. Uma lease expirada pode ser retomada, e só o detentor atual do token pode concluir uma execução. Uma falha HTTP registra um resultado `failed`, sem criar alteração.

`POST /api/monitors/{id}/checks` enfileira uma verificação manual e retorna `202 Accepted`; exige sessão e CSRF, monitor ativo e intervalo mínimo de cinco minutos entre pedidos manuais. `GET /api/monitors/{id}/checks` e `GET /api/monitors/{id}/changes` oferecem histórico paginado com filtros `from` e `to` em UTC; o histórico de verificações também aceita `result`. `GET /api/changes/{id}` retorna textos anterior/novo e trechos removidos/incluídos. `GET /api/dashboard` retorna contadores. `GET /api/notifications` lista alertas paginados, com filtro opcional `unread`; `PATCH /api/notifications/{id}/read` marca um alerta como lido e exige CSRF. Todos esses dados são filtrados pelo usuário autenticado.

A primeira leitura bem-sucedida cria o baseline. Leituras com texto normalizado idêntico não geram outra versão. Uma alteração cria snapshot, evento e notificação na mesma transação. Alterar a URL inicia novo baseline e preserva o histórico anterior. A limpeza por retenção permanece para uma etapa posterior, conforme permitido pelo MVP.
