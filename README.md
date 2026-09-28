<div align="center">

# Controle de Acesso de Veículos

Sistema web para digitalizar o registro, a consulta e a auditoria da movimentação de veículos no IFPE — Campus Belo Jardim.

[![CI Backend](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-backend.yml/badge.svg)](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-backend.yml)
[![CI Frontend](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-frontend.yml/badge.svg)](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-frontend.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
![Status](https://img.shields.io/badge/status-em%20desenvolvimento-yellow)

[Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki) · [Issues](https://github.com/ifpebj-ti/controle-acesso-veiculos/issues) · [Pull Requests](https://github.com/ifpebj-ti/controle-acesso-veiculos/pulls)

</div>

## Materiais acadêmicos e de acompanhamento

| Material | Finalidade | Acesso |
|---|---|---|
| Wiki do projeto | Visão, requisitos, arquitetura, segurança, testes, operação e andamento do projeto | [Acessar a Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki) |
| Protótipo no Figma | Fluxos, telas e validações de UX/UI do sistema | [Abrir o protótipo](https://www.figma.com/design/N6EOkXw8Ex7cZayyh4MJfY/Propotipagem?node-id=56-64&t=xA090z9jSE17HXUq-1) |
| Slides das weeklies | Apresentações semanais de acompanhamento do Projeto Integrador | [Ver no Canva](https://canva.link/tjsp5iu5c5iwbdp) |

Esses materiais apoiam o acompanhamento acadêmico. Decisões técnicas e
históricas que afetem o projeto devem continuar registradas no repositório e na
Wiki para preservar a rastreabilidade.

## Estado atual

> Atualizado em 13 de setembro de 2026. O MVP técnico está integrado localmente, mas ainda depende de homologação institucional e infraestrutura de produção antes do uso real na portaria.

| Área | Estado |
|---|---|
| Produto | MVP documentado para os Formulários nº 01 e nº 02; regras institucionais ainda precisam de validação |
| Frontend | Aplicação responsiva integrada à API para autenticação, renovação segura de sessão, acessos, histórico, correção, frota, motoristas, eventos e resumo diário; encerramento por inatividade compartilhada permanece na Issue #268 |
| Backend | API .NET 10 com autenticação, ciclo administrativo de contas, consulta administrativa da auditoria, fluxo geral, histórico e correção descritiva rastreável, manutenção de frota, motoristas, saída/retorno, histórico institucional, autorizações de eventos e resumo operacional diário |
| Dados | PostgreSQL 16, EF Core 10, treze entidades e treze migrations versionadas |
| Infraestrutura | Dockerfiles e Compose endurecidos, containers não privilegiados, Nginx com política de conteúdo e cabeçalhos defensivos verificados pela CI, build e scan por arquitetura, smoke test integrado, publicação multi-plataforma `linux/amd64` e `linux/arm64` no GHCR, proveniência assinada e SBOM SPDX por arquitetura atestado no digest do manifesto após integração na `main`, além de ensaio local de backup/restauração e exportação OpenTelemetry configurável |
| Qualidade | 189 testes automatizados no backend, incluindo PostgreSQL real, e 249 testes no frontend da `main`, com cobertura publicada pela CI |
| Segurança | JWT curto, sessões renováveis com rotação e revogação no servidor, cookies protegidos, CSRF, contas individuais, autorização por operação, rate limiting e auditoria transacional implementados; integração frontend da sessão, matriz final, retenção e imutabilidade em produção pendentes |
| Deploy | Imagens OCI versionadas no GHCR pela CI e base OTLP implementada; ambiente de homologação, HTTPS, collector, painéis, alertas, backup protegido e deploy ainda não configurados |

Os endpoints `/health`, `/health/live` e `/health/ready` são verificações técnicas. Autenticação, autorização e demais controles são exercitados diretamente nos contratos reais do produto; a API não mantém endpoints de exemplo do template ASP.NET Core.

Os contratos operacionais e administrativos disponíveis são:

| Método e rota | Finalidade |
|---|---|
| `POST /auth/login` | autentica uma conta ativa e retorna JWT, expiração e identidade mínima (`id`, e-mail e perfil) |
| `GET /auth/csrf` | emite o token antifalsificação necessário para renovar ou encerrar a sessão |
| `POST /auth/refresh` | rotaciona o refresh token em cookie `HttpOnly` e retorna um novo JWT curto |
| `POST /auth/logout` | revoga a família da sessão no servidor e expira o cookie |
| `GET /users` | pesquisa contas por nome/e-mail e estado, com paginação restrita a Administrador |
| `POST /users` | cria uma conta individual para um dos perfis preliminares do MVP |
| `DELETE /users/{id}` | desativa uma conta, revoga seus JWTs na próxima requisição e preserva o histórico |
| `POST /users/{id}/reactivation` | reativa a conta e limpa bloqueio temporário e tentativas anteriores |
| `GET /audits` | consulta a trilha por período, ação, entidade, registro e ator, restrita a Administrador |
| `GET /access-records/entry-candidates` | pesquisa pares ativos de veículo e condutor para o fluxo recorrente, com resposta mínima e limite de resultados |
| `POST /access-records/entries` | registra entrada e cria ou reutiliza pessoa, veículo, vínculo e categoria em uma transação; uma seleção recorrente é revalidada no servidor |
| `GET /access-records/open` | lista veículos com acesso ainda aberto |
| `GET /access-records/history` | pesquisa acessos por período, placa, condutor, categoria ou status para Portaria, Vigilância, Transporte e Administração |
| `POST /access-records/{id}/exit` | encerra um acesso usando horário e usuário autenticado do servidor |
| `POST /access-records/{id}/exceptional-closure` | regulariza uma saída não registrada sem inventar horário e mantém auditoria separada |
| `PUT /access-records/{id}/correction` | corrige objetivo, categoria e observação com justificativa para Porteiro, Vigilante e Administrador |
| `GET /institutional-vehicles` | lista a frota institucional ativa para operação e conferência |
| `POST /institutional-vehicles` | cadastra veículo institucional para `SetorTransporte` ou `Administrador` |
| `PUT /institutional-vehicles/{id}` | atualiza os dados da frota com auditoria transacional |
| `DELETE /institutional-vehicles/{id}` | inativa o veículo sem apagar viagens ou histórico |
| `POST /institutional-vehicles/{id}/reactivation` | reativa explicitamente um veículo institucional |
| `GET /institutional-drivers` | lista somente pessoas com autorização ativa para dirigir a frota |
| `POST /institutional-drivers` | autoriza um motorista para `SetorTransporte` ou `Administrador` |
| `DELETE /institutional-drivers/{id}` | revoga a autorização sem apagar seu histórico |
| `POST /institutional-vehicle-usages/departures` | registra a saída de veículo institucional e motorista já cadastrados |
| `GET /institutional-vehicle-usages/open` | lista usos institucionais ainda sem retorno |
| `GET /institutional-vehicle-usages/history` | pesquisa usos por período, veículo ou motorista para Transporte e Administrador |
| `POST /institutional-vehicle-usages/{id}/returns` | registra retorno e valida a quilometragem |
| `GET /event-authorizations` | pesquisa autorizações de eventos por período, nome e estado para os quatro perfis do MVP |
| `POST /event-authorizations` | cria uma autorização de evento somente para `Administrador` |
| `PUT /event-authorizations/{id}` | atualiza evento e regras de veículos na mesma transação auditada, somente para `Administrador` |
| `DELETE /event-authorizations/{id}` | cancela logicamente a autorização sem apagar seu histórico, somente para `Administrador` |
| `GET /operations/daily-summary` | resume entradas, saídas, usos institucionais e acessos vinculados a eventos no dia local informado |

A placa e a identificação de frota são normalizadas. O PostgreSQL impede duplicidades no catálogo, autorizações repetidas e dois acessos ou usos institucionais abertos para o mesmo veículo, inclusive em requisições concorrentes. As operações geram trilha de auditoria com operador, horário, registro e transição de estado na mesma transação; se a auditoria falhar, a operação é revertida. Nome do condutor, placa, objetivo e categoria são obrigatórios no fluxo geral. Porteiro, Vigilante e Administrador podem corrigir objetivo, categoria e observação com justificativa, sem alterar placa, condutor, horários, status ou autoria original. No fluxo institucional, o veículo deve estar ativo e a pessoa precisa de autorização explícita e ativa como motorista; revogar a autorização bloqueia novas saídas, mas não impede registrar o retorno de uma viagem aberta.

No processo confirmado para o MVP, dois Porteiros se revezam em jornadas de 12
horas e o Vigilante assume integralmente a operação, com as mesmas permissões,
quando não há Porteiro. Cada pessoa usa sua própria conta; registros abertos
garantem continuidade sem trocar a autoria original. Porteiro e Vigilante podem
corrigir apenas dados descritivos, sempre com justificativa e auditoria. O Setor
de Transporte supervisiona a portaria e consulta os históricos geral e
institucional, mas não registra, encerra nem corrige acessos gerais. O fechamento
por volta das 23h é uma regra operacional: o sistema não
bloqueia automaticamente o horário porque residentes e acessos previamente
autorizados podem constituir exceções.

Após a homologação, o registro digital deve ser a fonte principal. Formulários
em papel permanecem apenas para contingência diante de indisponibilidade e seus
dados devem ser reconciliados no sistema posteriormente; não se recomenda manter
dupla digitação permanente. O acervo físico anterior segue a retenção vigente até
aprovação de uma política institucional para os registros digitais.

## Problema e escopo do MVP

O controle atual depende de formulários físicos, arquivamento manual e consultas demoradas. O projeto busca preservar a rastreabilidade necessária e reduzir repetição e erros, sem copiar automaticamente toda a burocracia do papel.

O MVP está concentrado em:

- pessoas, veículos e seus vínculos;
- registro e consulta de entrada e saída;
- identificação de acessos ainda abertos;
- uso de veículos institucionais com motorista, quilometragem e itinerário;
- autorização e revogação de motoristas institucionais pelo setor responsável;
- consultas históricas paginadas para localizar acessos gerais e usos institucionais pelos filtros necessários;
- perfis, usuário responsável, correções descritivas rastreáveis e auditoria.

Reconhecimento automático de placas, câmeras, RFID, cancelas, estacionamento e os fluxos gerais de pedestres permanecem fora do primeiro incremento. Uma solução observada no mercado não se torna requisito sem levantamento local e validação do cliente.

## Arquitetura e stack

O repositório utiliza um monólito modular com Clean Architecture adaptada:

```text
React + TypeScript
        |
ASP.NET Core Web API
        |
Application -> Domain
        |
Infrastructure -> EF Core -> PostgreSQL
```

| Área | Tecnologias |
|---|---|
| Frontend | React 19, TypeScript, Vite 8, Tailwind CSS 4, React Router, Axios, React Hook Form e Zod |
| Backend | .NET 10, ASP.NET Core Web API e C# |
| Persistência | Entity Framework Core 10, Npgsql e PostgreSQL 16 |
| Testes | xUnit, Testcontainers, `WebApplicationFactory` e coverlet |
| Infraestrutura | Docker, Docker Compose, Nginx e GitHub Actions |
| Implantação futura | VM Linux na Oracle Cloud Infrastructure, ainda não configurada |

O Domain não referencia Entity Framework Core. O `DbContext`, as configurações Fluent API e as migrations permanecem em Infrastructure.

## Estrutura do repositório

```text
controle-acesso-veiculos/
├── .github/                  # workflows, templates e instruções
├── docs/                     # convenções e segurança versionada
├── infrastructure/
│   ├── database/             # backup e restauração verificável do PostgreSQL local
│   └── docker/               # Dockerfiles, Compose e configuração de exemplo
├── src/backend/
│   ├── ControleAcessoVeiculos.API/
│   ├── ControleAcessoVeiculos.Application/
│   ├── ControleAcessoVeiculos.Application.Tests/
│   ├── ControleAcessoVeiculos.Domain/
│   ├── ControleAcessoVeiculos.Domain.Tests/
│   ├── ControleAcessoVeiculos.Infrastructure/
│   └── ControleAcessoVeiculos.IntegrationTests/
└── src/frontend/             # aplicação React
```

## Pré-requisitos

- Git;
- .NET SDK 10;
- Node.js 24 LTS e npm;
- Docker Desktop ou Docker Engine com o plugin Compose;
- `dotnet-ef` 10 para criar ou aplicar migrations fora do container.

Instale ou atualize a ferramenta do EF Core:

```bash
dotnet tool update --global dotnet-ef --version 10.*
```

## Início rápido com Docker Compose

Crie o arquivo local de ambiente sem alterar o exemplo versionado:

```bash
cd infrastructure/docker
cp .env.example .env
```

No PowerShell, use:

```powershell
Set-Location infrastructure/docker
Copy-Item .env.example .env
```

Os valores do `.env.example` são exclusivamente locais e fictícios. Troque a senha no arquivo `.env`, que é ignorado pelo Git, antes de usar um ambiente compartilhado.

Valide, construa e suba os serviços:

```bash
docker compose config
docker compose build backend
docker compose up --build
```

Serviços padrão:

| Serviço | Endereço local |
|---|---|
| Frontend | `http://localhost:3000` |
| API | `http://localhost:8080` |
| PostgreSQL | `localhost:5432` |

O Compose não aplica migrations automaticamente. Em outro terminal, a partir da raiz do repositório, configure a conexão com os mesmos valores do seu `.env` e execute `dotnet ef database update` conforme a seção de migrations.

Para encerrar os containers sem apagar o volume do banco:

```bash
docker compose down
```

Não use `docker compose down --volumes` sem confirmar que os dados locais podem ser descartados.

### Compose com imagens publicadas

Para implantação, use o arquivo separado
`infrastructure/docker/docker-compose.production.yml`. Ele não recompila o
código no host: baixa do GHCR a mesma versão semântica do frontend e do backend,
mantém API e PostgreSQL somente nas redes internas e publica o frontend apenas
no loopback por padrão. Consulte o
[guia de implantação com imagens versionadas](docs/operations/versioned-container-deployment.md)
antes de executar `pull` ou promover uma release.

Esse Compose é uma base operacional reproduzível, não uma declaração de que o
ambiente institucional de produção já foi provisionado ou aprovado.

### Backup e restauração local

Com o PostgreSQL saudável e as migrations aplicadas, crie um dump lógico em
formato custom:

```powershell
./infrastructure/database/Backup-PostgreSql.ps1
```

Em seguida, comprove que ele pode ser restaurado:

```powershell
./infrastructure/database/Test-PostgreSqlRestore.ps1 `
  -BackupPath ./infrastructure/database/backups/controle-acesso-AAAAmmddTHHMMSSfffZ.dump
```

A verificação usa um banco temporário isolado e o remove ao terminar; ela nunca
substitui o banco operacional. Os dumps ficam em um diretório ignorado pelo Git,
mas podem conter dados pessoais e não são adequados para armazenamento de
produção sem criptografia e controle de acesso. Consulte o
[procedimento técnico](infrastructure/database/README.md), a
[proposta de retenção e continuidade](docs/operations/data-retention-and-continuity.md)
e as decisões institucionais ainda pendentes na Issue #30.

## Desenvolvimento local

### Banco PostgreSQL

Você pode executar apenas o banco pelo Compose:

```bash
cd infrastructure/docker
docker compose up -d postgresql
```

O arquivo `src/backend/ControleAcessoVeiculos.API/appsettings.Development.json` contém uma senha marcadora (`CHANGE_ME`), não uma credencial funcional. Prefira configurar a conexão apenas no ambiente local.

PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=controle_acesso;Username=controle_acesso_app;Password=change_this_local_password;GSS Encryption Mode=Disable'
```

Bash:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=controle_acesso;Username=controle_acesso_app;Password=change_this_local_password;GSS Encryption Mode=Disable'
```

Os valores acima correspondem ao exemplo local e devem ser substituídos fora do
ambiente de desenvolvimento. O modo GSS fica desabilitado localmente porque o
PostgreSQL usa autenticação por senha e não há Kerberos configurado. Isso evita
o aviso inofensivo de biblioteca GSSAPI ausente introduzido pelo Npgsql 10 e não
substitui criptografia de transporte. Em ambiente compartilhado ou de produção,
configure `SSL Mode=VerifyFull` com uma autoridade certificadora confiável ou
GSSAPI/Kerberos conforme a infraestrutura institucional.

Gere uma chave JWT local sem versioná-la. No PowerShell:

```powershell
$jwtKeyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Fill($jwtKeyBytes)
$env:Authentication__Jwt__SigningKey = [Convert]::ToBase64String($jwtKeyBytes)
```

No Bash:

```bash
export Authentication__Jwt__SigningKey="$(openssl rand -base64 48)"
```

Essa variável também é necessária para executar comandos `dotnet ef`, pois a API valida a configuração no startup. No Docker Compose, defina `JWT_SIGNING_KEY` no `.env` local conforme `.env.example`.

### Backend

A partir da raiz:

```bash
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet run --project src/backend/ControleAcessoVeiculos.API --launch-profile http
```

A API ficará em `http://localhost:5118`.

### Primeira conta administrativa

Depois de aplicar as migrations, configure temporariamente nome, e-mail fictício/local e uma senha de desenvolvimento com pelo menos 12 caracteres:

```powershell
$env:BootstrapAdmin__Name = 'Administrador Local'
$env:BootstrapAdmin__Email = 'admin.local@example.test'
$env:BootstrapAdmin__Password = 'substitua-por-uma-senha-local-segura'
dotnet run --project src/backend/ControleAcessoVeiculos.API -- --bootstrap-admin
Remove-Item Env:BootstrapAdmin__Name, Env:BootstrapAdmin__Email, Env:BootstrapAdmin__Password
```

O comando funciona somente enquanto não existir nenhum usuário. Ele cria a primeira conta fora da API HTTP. Depois disso, um Administrador autenticado pode consultar, criar, desativar e reativar contas em `/users`. A API impede auto-desativação e preserva ao menos um Administrador ativo. Não use dados ou senhas reais nos exemplos e não versione essas variáveis.

### Dados fictícios para demonstração

Com a API local saudável e o Administrador criado, prepare um conjunto
representativo sem acessar o banco diretamente:

```powershell
./infrastructure/demo/Initialize-DemoData.ps1 `
  -ApiBaseUrl http://127.0.0.1:5118
```

Para a API exposta pelo Compose, omita `-ApiBaseUrl` ou use a porta local
configurada. O script aceita somente loopback, solicita as credenciais do
Administrador e três senhas locais de demonstração distintas por prompt protegido, e não
imprime senha ou token. Ele cria contas e cenários fictícios abertos e
encerrados pelos endpoints normais da API. Consulte o
[guia dos dados de demonstração](infrastructure/demo/README.md) antes de executar.

O workflow `CI - Demo data` executa esse mesmo conjunto duas vezes contra API e
PostgreSQL descartáveis quando o backend, o Compose ou o inicializador mudam. A
barreira detecta incompatibilidades e duplicação antes da demonstração, mas não
substitui avaliação de usabilidade nem homologação com os perfis reais.

### Consultar a trilha de auditoria

Com um token de Administrador:

```bash
curl "http://localhost:5118/audits?fromUtc=2026-08-01T00:00:00Z&toUtc=2026-08-31T23:59:59Z&action=Alteracao&entity=Usuario&systemOnly=false&page=1&pageSize=25" \
  -H "Authorization: Bearer SEU_TOKEN"
```

Sem período, a consulta usa os últimos 30 dias; o intervalo máximo é de 90 dias.
`systemOnly=true` retorna apenas eventos sem ator humano, `false` retorna apenas
eventos associados a usuário e a omissão retorna ambos. A leitura não gera outro
evento de auditoria neste recorte. Essa decisão e o acesso exclusivo de
Administrador ainda precisam ser validados institucionalmente.

### Frontend

Em outro terminal:

```bash
cd src/frontend
npm ci
npm run dev
```

O Vite informará a URL de desenvolvimento, normalmente `http://localhost:5173`.

O cliente HTTP usa `/api` na mesma origem. Em desenvolvimento, o Vite encaminha
as requisições para `http://127.0.0.1:5118`; no Docker, o Nginx encaminha para o
container backend. Nos dois casos, o proxy remove somente o prefixo `/api`, pois
os endpoints da API estão publicados na raiz, como `/auth/login` e
`/access-records/open`.

### Testar o fluxo geral de acesso

Autentique uma conta de perfil operacional em `POST /auth/login`, copie o `accessToken` e use o arquivo [`ControleAcessoVeiculos.API.http`](src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.API.http) ou envie:

```bash
curl -X POST http://localhost:5118/access-records/entries \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"driverName":"Condutor Fictício","plate":"ABC-1D23","objective":"Visita técnica","categoryName":"Visitante"}'

curl http://localhost:5118/access-records/open \
  -H "Authorization: Bearer SEU_TOKEN"

curl "http://localhost:5118/access-records/history?plate=ABC-1D23&driverName=Condutor&categoryName=Visitante&status=Encerrado&page=1&pageSize=25" \
  -H "Authorization: Bearer SEU_TOKEN"

curl -X POST http://localhost:5118/access-records/1/exit \
  -H "Authorization: Bearer SEU_TOKEN"

curl -X POST http://localhost:5118/access-records/1/exceptional-closure \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"reason":"RegistroDeSaidaOmitido","observation":"Saída confirmada posteriormente pelo responsável operacional."}'

curl -X PUT http://localhost:5118/access-records/1/correction \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"objective":"Entrega autorizada","categoryName":"Entrega","observation":"Conferido","justification":"Categoria e objetivo conferidos pelo vigilante."}'
```

Os motivos aceitos no encerramento excepcional são `RegistroDeSaidaOmitido`,
`IndisponibilidadeDoSistema`, `OperacaoEmContingencia` e `Outro`. O horário
`observedExitAtUtc` é opcional e só deve ser informado quando houver fonte
confiável. Quando ausente, a API mantém a saída desconhecida e registra apenas o
momento da regularização. Porteiro, Vigilante e Administrador podem executar a
ação; o Setor de Transporte permanece em supervisão somente leitura.

As categorias preliminares aceitas são: `Visitante`, `Prestador de serviço`, `Entrega`, `Evento`, `Treino ou jogo`, `Caminhada com veículo`, `Mototáxi`, `Permanência excepcional` e `Outro acesso autorizado`. Elas são hipóteses do MVP e devem ser revistas após a validação com a portaria. A consulta histórica aceita filtros combináveis por placa, trecho do nome do condutor, categoria, status e período de entrada. Sem período, usa os últimos 30 dias; o intervalo máximo é de 366 dias e cada página contém de 1 a 100 registros. Documento pessoal, objetivo e observação não são parâmetros de busca. A correção exige justificativa e aceita registros abertos ou encerrados; placa, condutor, horários, status e autoria permanecem imutáveis até que outra regra seja validada com o cliente.

### Testar o fluxo de veículos institucionais

Com uma conta `SetorTransporte` ou `Administrador`, cadastre e consulte um veículo institucional:

```bash
curl -X POST http://localhost:5118/institutional-vehicles \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"plate":"IFP-1E23","identification":"FROTA-001","vehicleType":"Automóvel","brand":"Marca fictícia","model":"Modelo de teste","color":"Branco","year":2026}'

curl http://localhost:5118/institutional-vehicles \
  -H "Authorization: Bearer SEU_TOKEN"

curl -X PUT http://localhost:5118/institutional-vehicles/1 \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"plate":"IFP-1E23","identification":"FROTA-001","vehicleType":"Van","brand":"Marca fictícia","model":"Modelo de teste","color":"Branco","year":2026}'

curl -X DELETE http://localhost:5118/institutional-vehicles/1 \
  -H "Authorization: Bearer SEU_TOKEN"

curl -X POST http://localhost:5118/institutional-vehicles/1/reactivation \
  -H "Authorization: Bearer SEU_TOKEN"
```

`Porteiro` e `Vigilante` também podem consultar a frota ativa, mas não cadastrar veículos. Antes da primeira saída, `SetorTransporte` ou `Administrador` deve autorizar explicitamente o motorista. Documento é opcional no MVP; quando informado, tipo e número devem ser enviados juntos. Não use dados pessoais reais nos exemplos:

```bash
curl -X POST http://localhost:5118/institutional-drivers \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Motorista Fictício","documentType":"ID","documentNumber":"EXEMPLO-001"}'

curl http://localhost:5118/institutional-drivers \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

Na saída, use o campo `personId` retornado pela autorização como `driverId`. Para revogar novas saídas sem apagar o histórico, use `DELETE /institutional-drivers/{id}`, onde `id` é o identificador da autorização:

```bash
curl -X POST http://localhost:5118/institutional-vehicle-usages/departures \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"vehicleId":1,"driverId":1,"departureMileage":12500,"itinerary":"Campus - Unidade rural"}'

curl http://localhost:5118/institutional-vehicle-usages/open \
  -H "Authorization: Bearer SEU_TOKEN"

curl -X POST http://localhost:5118/institutional-vehicle-usages/1/returns \
  -H "Authorization: Bearer SEU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"returnMileage":12542}'

curl "http://localhost:5118/institutional-vehicle-usages/history?plate=IFP-1E23&from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z&page=1&pageSize=25" \
  -H "Authorization: Bearer SEU_TOKEN"
```

A consulta histórica aceita placa, identificação da frota, `vehicleId`, `driverId` e período combináveis. Sem período, usa os últimos 30 dias; o intervalo máximo é de 366 dias e cada página contém de 1 a 100 registros. A manutenção da frota é lógica e auditada: inativar bloqueia novas saídas, preserva viagens e não impede o retorno de uma viagem aberta. Critérios adicionais de elegibilidade de motoristas e correção autorizada de viagens permanecem em recortes futuros. A API não cria veículo institucional nem concede autorização implicitamente a partir de uma saída. CNH, validade, categoria, escala, assinatura, imagem e vínculo fixo com veículo não foram copiados das planilhas para o MVP sem necessidade institucional validada.

### Autorizações de eventos

`SetorTransporte` e `Administrador` podem cadastrar, atualizar e cancelar eventos.
`Porteiro` e `Vigilante` recebem somente leitura para conferir autorizações durante
a operação. Cada evento informa período, responsável, área, pernoite e de uma a
cem regras de veículos. Uma regra pode ser uma cota por tipo ou uma placa
normalizada específica; placa específica sempre representa um único veículo.

```bash
curl -X POST http://localhost:5118/event-authorizations \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"Evento de demonstração","responsible":"Coordenação responsável","startsAtUtc":"2026-09-10T12:00:00Z","endsAtUtc":"2026-09-11T02:00:00Z","area":"Pátio central","overnightAllowed":true,"vehicleRules":[{"vehicleType":"Automóvel","quantity":1,"plate":"ABC-1D23"},{"vehicleType":"Ônibus","quantity":3}]}'

curl "http://localhost:5118/event-authorizations?fromUtc=2026-09-01T00:00:00Z&toUtc=2026-09-30T23:59:59Z&active=true&page=1&pageSize=25" \
  -H "Authorization: Bearer $TOKEN"

curl -X POST http://localhost:5118/access-records/entries \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"driverName":"Condutor Fictício","plate":"ABC-1D23","objective":"Participação no evento","categoryName":"Evento","vehicleType":"Automóvel","eventAuthorizationId":1}'

curl -X DELETE http://localhost:5118/event-authorizations/1 \
  -H "Authorization: Bearer $TOKEN"
```

Sem período, a busca retorna eventos sobrepostos aos próximos 30 dias; o intervalo
máximo é de 366 dias. A auditoria registra ator, horários, estado, pernoite e
quantidade de regras, mas não duplica nome, responsável, área, observação ou
placas. Quando `eventAuthorizationId` é informado na entrada, o evento precisa
estar ativo e vigente. Placa específica tem precedência; caso não exista, uma cota
do tipo normalizado é consumida. O bloqueio transacional do evento impede excesso
silencioso sob concorrência. A saída não devolve a unidade consumida, e regras já
utilizadas não podem ser substituídas. Sem o identificador, o fluxo geral continua
inalterado. O sistema apoia a decisão da portaria e não abre o portão automaticamente.

### Resumo operacional diário

`Porteiro`, `Vigilante`, `SetorTransporte` e `Administrador` podem consultar um
resumo agregado para troca de turno e conferência. A data é opcional e usa o dia
atual da instituição quando omitida.

```bash
curl "http://localhost:5118/operations/daily-summary?date=2026-08-30" \
  -H "Authorization: Bearer $TOKEN"
```

A resposta informa entradas e saídas gerais, partidas e retornos institucionais,
acessos associados a eventos e quantos registros estavam abertos no começo e no
fim do dia. `openAtStart` preserva o trabalho recebido do turno anterior;
`openAtEnd` mostra a carga ainda pendente na virada do dia. O intervalo é
semiaberto (`periodStartUtc` inclusivo e `periodEndUtcExclusive` exclusivo), o que
evita contagem dupla entre dias consecutivos.

O fuso padrão é `America/Recife` e pode ser alterado por
`Institution__TimeZoneId`. O endpoint não expõe nomes, documentos, placas,
itinerários ou observações e não classifica automaticamente atrasos ou
irregularidades. Jornada de 12 horas, fechamento por volta das 23h e exceções
institucionais continuam sendo regras operacionais a validar com o cliente, não
limites rígidos do sistema.

## Migrations

Execute a partir da raiz, com `ConnectionStrings__DefaultConnection` configurada.

Aplicar migrations pendentes:

```bash
dotnet ef database update --project src/backend/ControleAcessoVeiculos.Infrastructure --startup-project src/backend/ControleAcessoVeiculos.API
```

Criar uma migration depois de aprovar uma mudança de modelo:

```bash
dotnet ef migrations add NomeDaMigration --project src/backend/ControleAcessoVeiculos.Infrastructure --startup-project src/backend/ControleAcessoVeiculos.API --output-dir Data/Migrations
```

Nunca edite ou remova uma migration já aplicada em ambiente compartilhado sem plano de migração e revisão.

## Validação e testes

Backend:

```bash
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet test src/backend/ControleAcessoVeiculos.slnx --no-build --no-restore
```

Frontend:

```bash
cd src/frontend
npm ci
npm run lint
npm run build
```

Verificações técnicas da API:

```bash
curl http://localhost:5118/health
curl http://localhost:5118/health/live
curl http://localhost:5118/health/ready
curl -i http://localhost:5118/access-records/open
```

`/health` e `/health/live` verificam o processo HTTP. `/health/ready` também verifica o PostgreSQL e retorna HTTP 503 quando o banco não está acessível. `/access-records/open` retorna HTTP 401 sem token e permite conferir a proteção de um contrato real do produto.

### Observabilidade opcional

A API exporta métricas HTTP/runtime e traces ASP.NET Core por OTLP quando
`OTEL_ENABLED=true`. O recurso permanece desabilitado por padrão e o projeto não
inclui um collector. Antes de habilitar, disponibilize um receiver acessível e
configure `OTEL_EXPORTER_OTLP_ENDPOINT`; health checks são excluídos dos traces.

```dotenv
OTEL_ENABLED=true
OTEL_SERVICE_NAME=controle-acesso-veiculos-api
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
```

Não coloque token em URL, Compose ou arquivo versionado. Se o collector exigir
headers de autenticação, injete `OTEL_EXPORTER_OTLP_HEADERS` por secret manager.
Consulte o [guia de observabilidade](docs/operations/observability.md) para
validação, privacidade, alertas propostos e pendências de produção.

## Configuração e segurança

- Toda resposta inclui `X-Correlation-ID`; somente UUIDs válidos enviados pelo cliente são reutilizados.
- Erros inesperados usam `application/problem+json` sem mensagem interna ou stack trace.
- O corpo de cada requisição é limitado globalmente a 1 MiB.
- A API aceita por padrão 300 requisições por minuto por usuário autenticado ou endereço da conexão; o login aceita 30 por minuto por endereço, sem fila.
- Excesso retorna HTTP 429 em `ProblemDetails`, com correlação e `Retry-After`; health checks não são limitados.
- Os limites podem ser sobrescritos por `RateLimiting__GlobalPermitLimit`, `RateLimiting__GlobalWindowSeconds`, `RateLimiting__LoginPermitLimit` e `RateLimiting__LoginWindowSeconds`.
- A API não confia em `X-Forwarded-For`; configure proxies conhecidos antes de usar o endereço original encaminhado em produção.
- Logs HTTP incluem correlação, método, template da rota, status e duração; não incluem valores da URL, query string, corpo nem cabeçalho de autorização.
- A auditoria dos fluxos geral, institucional e dos catálogos de frota, motoristas e eventos registra somente identificadores e estados necessários; não duplica nome, documento, responsável, placa, identificação patrimonial, objetivo, observação nem itinerário.
- Login bem-sucedido e bloqueio temporário de conta geram auditoria transacional sem e-mail, senha, hash, token ou IP; falha da auditoria impede emitir o token.
- Desativação e reativação de conta exigem Administrador, são auditadas na mesma transação e registram somente a mudança do estado `active`.
- Criação administrativa e bootstrap também são auditados atomicamente; a criação HTTP registra o Administrador, enquanto o bootstrap usa ator de sistema nulo sem atribuição falsa.
- A trilha pode ser consultada apenas por Administrador por meio da política dedicada `audits:read`, com período máximo de 90 dias, paginação e filtros; a resposta não faz joins com dados de pessoa, conta ou veículo e projeta somente os campos já persistidos na auditoria. Justificativas de correção fazem parte da trilha e não devem conter dados pessoais desnecessários.
- Cada requisição autenticada confirma no banco se a conta e o perfil continuam ativos; assim, um JWT emitido antes da desativação deixa de autorizar imediatamente.
- O refresh token nunca integra o JSON nem o armazenamento web: somente seu hash fica no PostgreSQL e o valor bruto permanece em cookie `HttpOnly`; renovação e logout exigem proteção CSRF.
- Não versione `.env`, `.env.local`, tokens, chaves ou connection strings reais.
- Use `ConnectionStrings__DefaultConnection` para sobrescrever a configuração local.
- Não use dados pessoais reais em testes, seeds, exemplos, issues ou capturas de tela.
- O frontend recebe apenas variáveis prefixadas por `VITE_`; elas não podem conter segredos.
- Revise migrations, permissões e logs antes de usar dados institucionais.
- Os primeiros fluxos de negócio, a manutenção inicial da frota, o catálogo de motoristas e as consultas históricas geral e institucional existem, mas a matriz final de perfis, recuperação e outros casos de uso ainda estão em desenvolvimento; o sistema não deve ser exposto publicamente.

Consulte a [modelagem de ameaças](docs/security/threat-model.md), o [guia de desenvolvimento seguro](docs/security/secure-development-guide.md) e as [instruções de segurança](.github/instructions/security.instructions.md).
As decisões e pendências da fundação de login estão em [autenticação e autorização](docs/security/authentication.md).
O desenho de renovação, rotação e revogação está registrado no [ADR 0001 — ciclo de vida seguro de sessões](docs/architecture/decisions/0001-secure-session-lifecycle.md).

## Documentação

- [Materiais acadêmicos e de acompanhamento](#materiais-acadêmicos-e-de-acompanhamento).
- [Convenções de commit](docs/development/commit-conventions.md).
- [CI/CD e segurança de containers](docs/development/ci-cd.md).
- [Observabilidade da API](docs/operations/observability.md).
- [Roteiro de homologação do backend do MVP](docs/validation/backend-mvp-homologation.md).
- [Possibilidades de evolução acadêmica](docs/research/academic-evolution-options.md) — material exploratório para uma futura conversa com professores; não integra o escopo atual.
- [Modelagem de ameaças](docs/security/threat-model.md).
- [Guia de desenvolvimento seguro](docs/security/secure-development-guide.md).
- [ADR 0001 — ciclo de vida seguro de sessões](docs/architecture/decisions/0001-secure-session-lifecycle.md).

A Wiki abrange o projeto completo, não apenas o backend. Documentos acadêmicos e decisões históricas devem ser atualizados de forma aditiva, preservando contexto e rastreabilidade.

## Contribuição

1. Confirme ou abra uma Issue com escopo e critérios de aceite.
2. Crie uma branch vinculada à Issue.
3. Faça commits pequenos conforme as [convenções](docs/development/commit-conventions.md).
4. Execute as validações proporcionais à mudança.
5. Abra uma Pull Request com evidências e riscos conhecidos.
6. Aguarde a revisão e os checks obrigatórios; não contorne a proteção da `main`.

Papéis principais:

| Pessoa | Responsabilidade principal | Apoio |
|---|---|---|
| [Raíssa Beatriz](https://github.com/Raissa-Beatriz) | Frontend e UX/UI | DevOps, infraestrutura e QA |
| [José Ernandes](https://github.com/ErnandesCosta) | Backend e banco de dados | DevOps, infraestrutura e QA |

## Licença

Distribuído sob a [Apache License 2.0](LICENSE).
