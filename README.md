# WebChange Radar

Aplicação para acompanhar alterações no texto de páginas públicas, com histórico, comparação e alertas internos ou por e-mail. Usa ASP.NET Core em .NET 10, PostgreSQL, Entity Framework Core e Next.js. O escopo atual está em [docs/V1.md](docs/V1.md); [docs/MVP.md](docs/MVP.md) registra as decisões do MVP.

## Estrutura

- `backend/src/WebChangeRadar.Api`: API e módulos por funcionalidade.
- `backend/tests/WebChangeRadar.UnitTests`: testes unitários das regras de negócio.
- `backend/tests/WebChangeRadar.IntegrationTests`: testes HTTP com `WebApplicationFactory`.
- `frontend`: interface Next.js, TypeScript, Tailwind CSS e componentes shadcn/ui.
- `docs/PROGRESS.md`: progresso da implementação.

## Executar

Instale o SDK do .NET 10 e PostgreSQL local. Copie `.env.example` para `.env` e configure as conexões de `webchange_dev` e `webchange_test` para o usuário `webchange_app`. A API carrega esse arquivo automaticamente apenas em `Development`; variáveis de ambiente definidas no processo têm prioridade. Em produção, use variáveis de ambiente normais. Execute na raiz do repositório:

Use três terminais, com PostgreSQL já disponível. No primeiro, inicie a API com o comando `dotnet run` abaixo. No segundo, inicie o frontend com os comandos da seção **Frontend local**. No terceiro, na raiz do repositório, inicie somente o agendador:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./dev.ps1
```

O agendador espera a API em `https://localhost:7010`, chama `POST /internal/jobs/process-due` imediatamente e a cada minuto, e não inicia nem encerra a API ou o frontend. Ele lê `InternalJobs__Token` do ambiente ou de `.env`, envia o valor apenas no cabeçalho de autorização e não imprime o segredo. As chamadas são sequenciais: se uma durar mais de um minuto, a próxima espera e os disparos intermediários são pulados. `Ctrl+C` encerra apenas o agendador. Para testar uma chamada contra uma API já iniciada, use `./dev.ps1 -Once`.

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
$env:NODE_EXTRA_CA_CERTS = (Resolve-Path .\certificates\api-dev.pem).Path
npm.cmd run dev
```

Acesse a URL HTTPS impressa pelo Next.js, normalmente `https://localhost:3000` (ou `3001` se a porta estiver ocupada), e aceite o certificado de desenvolvimento na primeira visita. Confirme que o Next iniciou em **HTTPS**; se ele cair para HTTP por falha ao gerar o certificado, resolva essa falha antes de testar o login. O Next encaminha `/api/*` para `https://localhost:7010` por padrão. Se usar outra origem, configure `API_ORIGIN` com uma URL **HTTPS** antes de iniciar o frontend; valores HTTP são rejeitados. A variável `NODE_EXTRA_CA_CERTS` precisa ser definida **no terminal do frontend antes de iniciar o Next**, para que o Node confie no certificado público da API e o proxy não retorne HTTP 500. Não desative a verificação TLS. Em produção, defina `API_ORIGIN` como a origem HTTPS da API e sirva o Next.js por HTTPS. O navegador acessa apenas a origem do Next; assim os cookies `Secure`, `HttpOnly` e `SameSite=Strict` e o token CSRF funcionam sem CORS nem acesso aos cookies pelo JavaScript. O servidor Next precisa estar acessível durante o uso da interface, pois faz o proxy da API.

O arquivo `frontend/certificates/api-dev.pem` contém apenas o certificado público da API e é ignorado pelo Git; não inclua a chave privada.

O frontend oferece cadastro, login, dashboard, gestão de monitoramentos, verificações manuais, histórico, comparação e notificações. Dados do painel e das notificações são atualizados a cada 30 segundos enquanto a aba está visível, ao retornar a ela e pelo botão **Atualizar**. Essa atualização consulta a API; não coleta páginas externas. Verificações manuais são enfileiradas; o resultado depende da chamada periódica ao endpoint interno documentado abaixo. Para verificar o frontend, execute em `frontend`: `npm.cmd run typecheck`, `npm.cmd run lint`, `npm.cmd test` e `npm.cmd run build` no PowerShell, ou `npm run ...` em outros shells.

Como alternativa ao PostgreSQL instalado no Windows, `docker compose up -d` inicia o PostgreSQL em contêiner e cria os dois bancos na primeira inicialização do volume. Escolha uma porta livre em `POSTGRES_PORT` se já houver um servidor local em 5432. Um volume antigo não executa novamente o script de criação do banco de testes.

`ApplicationDbContext` é o contexto do EF Core: ele mapeia usuários do Identity e monitoramentos para o PostgreSQL. As migrations versionadas criam as tabelas e as restrições. Não armazene credenciais em arquivos versionados.

Os testes PostgreSQL usam `WEBCHANGE_TEST_CONNECTION`, carregada do ambiente ou do `.env`. O usuário precisa poder criar esquemas em `webchange_test`; cada fixture cria e remove seu próprio esquema. A conexão de teste deve apontar para um banco diferente da conexão de desenvolvimento. Sem essa configuração, os testes que exigem PostgreSQL são ignorados. O CI fornece um contêiner temporário.

## Autenticação

As rotas ficam sob `/api/auth`: `POST /register`, `POST /login`, `POST /logout` e `GET /me`. Cadastro cria a conta e inicia a sessão. `/me` e `/logout` exigem autenticação. Identity gerencia hashes de senha e bloqueio temporário após tentativas incorretas. Login e cadastro têm limite de dez requisições por minuto por IP.

A interface acessa a API por **mesma origem**, via proxy, usando HTTPS. Antes de cada escrita, faça `GET /api/auth/csrf` e envie o `requestToken` recebido no cabeçalho `X-CSRF-TOKEN`. Após cadastro, login ou logout, obtenha um novo token. Os cookies de sessão e CSRF são `Secure`, `HttpOnly` e `SameSite=Strict`; o JavaScript não precisa lê-los. O navegador envia os cookies automaticamente nas requisições da mesma origem.

Para uma implantação com várias instâncias, será necessário compartilhar as chaves de Data Protection que assinam os cookies. A configuração de produção ainda não faz parte desta etapa.

## Monitoramentos

As rotas autenticadas em `/api/monitors` permitem criar, listar, consultar, editar e excluir monitoramentos. `PATCH /api/monitors/{id}/status` recebe `{"status":"paused"}` ou `{"status":"active"}`. Criação e edição recebem `name`, `url` e `intervalMinutes` (60, 360 ou 1440), além dos campos opcionais `contentSelector`, `keywords` (até dez expressões de até 60 caracteres) e `emailAlertsEnabled`. Há no máximo três monitoramentos por usuário. Escritas exigem o cabeçalho CSRF descrito acima.

A listagem aceita `status=active|paused`, `page` (a partir de 1) e `pageSize` (1 a 100, padrão 20). Retorna `items`, `page`, `pageSize` e `totalCount`. O monitor novo começa ativo e disponível para a primeira verificação imediatamente. Alterar a URL ou o seletor marca uma nova linha de base e antecipa a próxima verificação; alterar somente o intervalo a agenda a partir da atualização. Ao pausar, `nextCheckAt` fica nulo; reativar agenda uma verificação imediata. O histórico anterior é preservado.

Os contratos HTTP usam DTOs (`MonitorRequest` e `MonitorResponse`) para não expor diretamente a entidade do EF Core. `TimeProvider` fornece o horário UTC para agendamento e facilita testes determinísticos. Uma transação com bloqueio da linha do usuário mantém o limite de três monitores mesmo com criações simultâneas.

## Verificações, histórico e notificações

Configure `InternalJobs__Token` com um segredo aleatório. Em desenvolvimento, `dev.ps1` chama `POST /internal/jobs/process-due` com `Authorization: Bearer <segredo>` a cada minuto. O endpoint permanece disponível para um agendador externo: cria trabalhos para monitores vencidos e processa até dez verificações por chamada. O banco guarda trabalhos pendentes, leases temporárias e resultados. Uma lease expirada pode ser retomada, e só o detentor atual do token pode concluir uma execução. Uma falha HTTP registra um resultado `failed`, sem criar alteração.

### Preparação para Google Cloud Scheduler

Em um futuro deploy, crie **um** job HTTP com cron `* * * * *`, método `POST` e destino HTTPS `/internal/jobs/process-due`. Use uma conta de serviço dedicada com `roles/run.invoker` no serviço Cloud Run e configure token OIDC com audiência igual à URL base do serviço. O Cloud Scheduler envia esse token no cabeçalho `Authorization`. Antes do deploy, adapte a autenticação da rota para aceitar a identidade OIDC validada pelo Cloud Run **sob uma configuração explícita de produção**, mantendo o segredo Bearer para o desenvolvimento local; o endpoint atual exige o segredo Bearer e ainda não aceita OIDC como substituto. Não exponha o serviço sem a proteção IAM nem grave segredos na definição do job. Configure o timeout e monitore falhas, atrasos e custo. O deploy não faz parte desta etapa. Consulte a [documentação de autenticação do Cloud Scheduler](https://docs.cloud.google.com/scheduler/docs/http-target-auth) e a [integração com Cloud Run](https://docs.cloud.google.com/run/docs/triggering/using-scheduler).

`POST /api/monitors/{id}/checks` enfileira uma verificação manual e retorna `202 Accepted`; exige sessão e CSRF e monitor ativo. O intervalo mínimo entre pedidos manuais é configurado por `ManualCheckCooldownMinutes`, com padrão de cinco minutos. Em `Development`, defina `ManualCheckCooldownMinutes=0` no `.env` para permitir pedidos consecutivos; fora de `Development`, o mínimo continua sendo cinco minutos. `GET /api/monitors/{id}/checks` e `GET /api/monitors/{id}/changes` oferecem histórico paginado com filtros `from` e `to` em UTC; o histórico de verificações também aceita `result`. `GET /api/changes/{id}` retorna textos anterior/novo e trechos removidos/incluídos. `GET /api/dashboard` retorna contadores. `GET /api/notifications` lista alertas paginados, com filtro opcional `unread`; `PATCH /api/notifications/{id}/read` marca um alerta como lido e exige CSRF. Todos esses dados são filtrados pelo usuário autenticado.

A primeira leitura bem-sucedida cria o baseline. Leituras com texto normalizado idêntico não geram outra versão. Uma alteração cria snapshot e evento na mesma transação, com alerta interno somente se o filtro de palavras-chave corresponder. Alterar a URL ou o seletor inicia novo baseline e preserva o histórico anterior. A limpeza por retenção permanece para uma etapa posterior.

## Seletor CSS e palavras-chave

O campo **Seletor CSS** aceita, por exemplo, `main`, `.noticias` ou `#avisos`. Apenas o texto visível dos elementos encontrados entra no hash e no diff. Uma área sem correspondência registra `selector_no_match` na verificação, sem criar snapshot vazio ou alerta falso. Mudanças fora da área selecionada não alteram o histórico. Um seletor inválido é rejeitado ao salvar. Trocar o seletor redefine o baseline; trocar somente as palavras-chave não o redefine.

As palavras-chave são expressões separadas por linha ou vírgula no formulário. Sem elas, toda mudança real gera alerta. Com elas, uma mudança gera alerta se o **novo texto monitorado** contém qualquer expressão, sem diferenciar maiúsculas e minúsculas. O termo pode já estar presente em uma parte que não mudou. Mudanças que não correspondem ainda aparecem no histórico e no diff.

## Alertas por e-mail

Ative **Alertas por e-mail** no monitor. Apenas mudanças reais que passam pelo filtro de palavras-chave geram um pedido de envio. O endereço de destino é o e-mail confirmado da conta; baseline, falhas e contas sem confirmação não geram e-mail. O pedido entra na tabela `EmailOutbox` na mesma transação do evento. O endpoint central processa depois até dez entregas por chamada, com claim concorrente, lease, até cinco tentativas e backoff. Pedidos pendentes por mais de 24 horas expiram, evitando envio tardio após longa indisponibilidade. Os estados `pending`, `sending`, `sent` e `failed` ficam no banco; o painel de notificações mostra pendências e falhas da conta. Falhas de entrega não removem a notificação interna. Uma chave única por alteração evita dois pedidos lógicos, mas o protocolo SMTP não garante entrega exatamente uma vez após uma interrupção entre envio e gravação de `sent`.

Sem configuração de e-mail, a API continua funcionando e o canal aparece como indisponível. Para testar sem enviar mensagens reais, acrescente ao `.env` em `Development`:

```dotenv
Email__Mode=File
Email__FileDirectory=.local-emails
Email__PublicBaseUrl=https://localhost:3000
```

Reinicie a API, cadastre-se e, no formulário de monitor, clique em **Enviar confirmação**. Abra o arquivo novo em `.local-emails` e visite o link de confirmação. Ative os alertas no monitor, faça uma leitura inicial e altere uma página HTML pública sob seu controle. `localhost` e IPs privados são bloqueados pela proteção SSRF. Após `powershell -NoProfile -ExecutionPolicy Bypass -File ./dev.ps1 -Once` ou a próxima rodada do scheduler, confira a notificação interna e o novo arquivo de e-mail. O arquivo de sandbox é ignorado pelo Git e pode conter o endereço da conta e a URL monitorada.

Para SMTP real, defina `Email__Mode=Smtp`, `Email__Host`, `Email__Port`, `Email__From`, `Email__Username`, `Email__Password` e `Email__PublicBaseUrl` (origem HTTPS do frontend), com segredos apenas no ambiente ou secret manager. O envio usa TLS. Não há provedor contratado ou entrega real habilitada neste repositório. Revise verificação de domínio, limites do provedor e prevenção de abuso antes de permitir cadastros públicos.

## Validação e publicação

Os testes do backend usam fakes de rede e PostgreSQL separado; execute `dotnet test WebChangeRadar.slnx`. No frontend, execute `npm.cmd run typecheck`, `npm.cmd run lint`, `npm.cmd test` e `npm.cmd run build` no PowerShell. Há workflows para backend e frontend, mas a execução remota do CI precisa ser verificada no GitHub após push autorizado.

Para validação manual, use uma página HTML pública de teste sob seu controle: crie o monitor com seletor `main` e palavra-chave `lançamento`; rode a primeira checagem, altere apenas um `aside` e confirme que não há mudança; altere o texto de `main` sem o termo e confirme histórico sem alerta; altere `main` incluindo o termo e confirme alerta. A lista e as notificações devem se atualizar em até cerca de 30 segundos com a aba visível ou ao clicar **Atualizar**. Um monitor vencido exibe **aguardando verificação** até a conclusão real. O scheduler local continua sendo único, a cada minuto, e respeita a frequência de cada monitor. Em 30 dias, um job externo por minuto representa aproximadamente 43.200 invocações para todos os usuários juntos; as coletas HTTP são consumo adicional.

O deploy permanece pendente: validar uma hospedagem que execute o servidor Next.js com proxy de mesma origem, configurar Data Protection compartilhado, adaptar autenticação OIDC do endpoint interno para Cloud Scheduler/Cloud Run, definir banco gerenciado, retenção e rollback, e conferir preços e cotas atuais. Não há infraestrutura publicada nem recursos pagos ativados.
