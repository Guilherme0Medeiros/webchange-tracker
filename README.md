# WebChange Radar

Aplicação para acompanhar alterações no texto de páginas públicas. O back-end usa ASP.NET Core em .NET 10, PostgreSQL e Entity Framework Core. O produto completo está especificado em [docs/MVP.md](docs/MVP.md).

## Estrutura

- `backend/src/WebChangeRadar.Api`: API e módulos por funcionalidade.
- `backend/tests/WebChangeRadar.UnitTests`: testes unitários para as regras de negócio das próximas etapas.
- `backend/tests/WebChangeRadar.IntegrationTests`: testes HTTP com `WebApplicationFactory`.
- `frontend`: interface Next.js, TypeScript, Tailwind CSS e componentes shadcn/ui.
- `docs/PROGRESS.md`: progresso da implementação.

## Executar

Instale o SDK do .NET 10 e PostgreSQL local. Copie `.env.example` para `.env` e configure as conexões de `webchange_dev` e `webchange_test` para o usuário `webchange_app`. A API carrega esse arquivo automaticamente apenas em `Development`; variáveis de ambiente definidas no processo têm prioridade. Em produção, use variáveis de ambiente normais. Execute na raiz do repositório:

Para iniciar API, frontend e agendador local juntos, com PostgreSQL já disponível, instale as dependências do frontend (`npm install` em `frontend`) e execute `./dev.ps1` na raiz. Se a política do PowerShell bloquear scripts, use `powershell -NoProfile -ExecutionPolicy Bypass -File ./dev.ps1`; essa opção vale apenas para o processo iniciado. O script usa HTTPS em `https://localhost:7010`, espera a API ficar pronta e chama `POST /internal/jobs/process-due` imediatamente e a cada minuto. Ele lê `InternalJobs__Token` do ambiente ou de `.env`, envia o valor apenas no cabeçalho de autorização e não imprime o segredo. As chamadas são sequenciais: se uma durar mais de um minuto, a próxima espera e os disparos intermediários são pulados. `Ctrl+C` encerra o agendador e os processos da API e do frontend iniciados pelo script. Para testar uma única chamada contra uma API já iniciada, use `./dev.ps1 -SchedulerOnly -Once`.

Para testar frequências mais rápidas, acrescente `DevelopmentCheckIntervalMinutes=2` ao `.env` e reinicie a API. O valor aceito é de 1 a 59 minutos, somente em `Development`. O horário efetivo de `nextCheckAt` é calculado a partir do agendamento normal salvo, inclusive para monitores anteriores à configuração; a preferência de 1 h, 6 h ou 24 h permanece no banco. Ao remover a variável e reiniciar, o horário e a seleção dos vencidos voltam ao intervalo normal. O agendador central continua com cadência de um minuto. O horário exibido é previsto; trabalho pendente, duração da busca e indisponibilidade da API podem atrasar a execução.

```powershell
dotnet tool restore
dotnet ef database update --project backend/src/WebChangeRadar.Api --startup-project backend/src/WebChangeRadar.Api
dotnet build --verbosity quiet
dotnet test --verbosity quiet
dotnet run --project backend/src/WebChangeRadar.Api --launch-profile https
```

A API escuta em `https://localhost:7010` no perfil HTTPS. Para confiar no certificado local, execute `dotnet dev-certs https --trust`. `GET /health` retorna `200 OK` com o texto `Healthy`. Em desenvolvimento, o documento OpenAPI fica em `/openapi/v1.json`.

### Frontend local

Com a API e o PostgreSQL em execução, em outro terminal:

```powershell
cd frontend
npm install
npm run dev
```

Acesse `https://localhost:3000` e aceite o certificado de desenvolvimento do Next.js na primeira visita. Confirme que o Next iniciou em **HTTPS**; se ele cair para HTTP por falha ao gerar o certificado, resolva essa falha antes de testar o login. Use `npm.cmd` no lugar de `npm` se o PowerShell bloquear scripts. O Next encaminha `/api/*` para `https://localhost:7010` por padrão. Se usar outra origem, configure `API_ORIGIN` com uma URL **HTTPS** antes de iniciar o frontend; valores HTTP são rejeitados. O processo Node do Next também precisa confiar no certificado TLS da API. Em desenvolvimento, confie no certificado da API e, se o Node ainda o rejeitar, defina `NODE_EXTRA_CA_CERTS` para um arquivo PEM contendo apenas o certificado público da API antes de iniciar o Next. Não desative a verificação TLS. Em produção, defina `API_ORIGIN` como a origem HTTPS da API e sirva o Next.js por HTTPS. O navegador acessa apenas a origem do Next; assim os cookies `Secure`, `HttpOnly` e `SameSite=Strict` e o token CSRF funcionam sem CORS nem acesso aos cookies pelo JavaScript. O servidor Next precisa estar acessível durante o uso da interface, pois faz o proxy da API.

Exemplo no PowerShell, após salvar o certificado público da API em `frontend/certificates/api-dev.pem`: `$env:NODE_EXTRA_CA_CERTS = (Resolve-Path .\certificates\api-dev.pem).Path` (execute dentro de `frontend` e reinicie o Next). Esse arquivo local é ignorado pelo Git; não inclua a chave privada.

O frontend oferece cadastro, login, dashboard, gestão de monitoramentos, verificações manuais, histórico, comparação e notificações. Verificações manuais são enfileiradas; o resultado depende da chamada periódica ao endpoint interno documentado abaixo. Para verificar o frontend, execute em `frontend`: `npm run typecheck`, `npm run lint` e `npm run build`.

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

Configure `InternalJobs__Token` com um segredo aleatório. Em desenvolvimento, `dev.ps1` chama `POST /internal/jobs/process-due` com `Authorization: Bearer <segredo>` a cada minuto. O endpoint permanece disponível para um agendador externo: cria trabalhos para monitores vencidos e processa até dez verificações por chamada. O banco guarda trabalhos pendentes, leases temporárias e resultados. Uma lease expirada pode ser retomada, e só o detentor atual do token pode concluir uma execução. Uma falha HTTP registra um resultado `failed`, sem criar alteração.

### Preparação para Google Cloud Scheduler

Em um futuro deploy, crie **um** job HTTP com cron `* * * * *`, método `POST` e destino HTTPS `/internal/jobs/process-due`. Use uma conta de serviço dedicada com `roles/run.invoker` no serviço Cloud Run e configure token OIDC com audiência igual à URL base do serviço. O Cloud Scheduler envia esse token no cabeçalho `Authorization`. Antes do deploy, adapte a autenticação da rota para aceitar a identidade OIDC validada pelo Cloud Run **sob uma configuração explícita de produção**, mantendo o segredo Bearer para o desenvolvimento local; o endpoint atual exige o segredo Bearer e ainda não aceita OIDC como substituto. Não exponha o serviço sem a proteção IAM nem grave segredos na definição do job. Configure o timeout e monitore falhas, atrasos e custo. O deploy não faz parte desta etapa. Consulte a [documentação de autenticação do Cloud Scheduler](https://docs.cloud.google.com/scheduler/docs/http-target-auth) e a [integração com Cloud Run](https://docs.cloud.google.com/run/docs/triggering/using-scheduler).

`POST /api/monitors/{id}/checks` enfileira uma verificação manual e retorna `202 Accepted`; exige sessão e CSRF e monitor ativo. O intervalo mínimo entre pedidos manuais é configurado por `ManualCheckCooldownMinutes`, com padrão de cinco minutos. Em `Development`, defina `ManualCheckCooldownMinutes=0` no `.env` para permitir pedidos consecutivos; fora de `Development`, o mínimo continua sendo cinco minutos. `GET /api/monitors/{id}/checks` e `GET /api/monitors/{id}/changes` oferecem histórico paginado com filtros `from` e `to` em UTC; o histórico de verificações também aceita `result`. `GET /api/changes/{id}` retorna textos anterior/novo e trechos removidos/incluídos. `GET /api/dashboard` retorna contadores. `GET /api/notifications` lista alertas paginados, com filtro opcional `unread`; `PATCH /api/notifications/{id}/read` marca um alerta como lido e exige CSRF. Todos esses dados são filtrados pelo usuário autenticado.

A primeira leitura bem-sucedida cria o baseline. Leituras com texto normalizado idêntico não geram outra versão. Uma alteração cria snapshot, evento e notificação na mesma transação. Alterar a URL inicia novo baseline e preserva o histórico anterior. A limpeza por retenção permanece para uma etapa posterior, conforme permitido pelo MVP.
