<div align="center">

# Controle de Acesso de Veículos

Sistema web para registrar, consultar e auditar a movimentação de veículos no
IFPE — Campus Belo Jardim.

[![CI Backend](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-backend.yml/badge.svg)](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-backend.yml)
[![CI Frontend](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-frontend.yml/badge.svg)](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-frontend.yml)
[![CI Containers](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-containers.yml/badge.svg)](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/ci-containers.yml)
[![Release](https://img.shields.io/github/v/release/ifpebj-ti/controle-acesso-veiculos?display_name=tag)](https://github.com/ifpebj-ti/controle-acesso-veiculos/releases)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

[Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki) ·
[Documentação](#documentação) ·
[Release atual](https://github.com/ifpebj-ti/controle-acesso-veiculos/releases/tag/v0.3.0) ·
[Issues](https://github.com/ifpebj-ti/controle-acesso-veiculos/issues)

</div>

> [!IMPORTANT]
> O MVP técnico está integrado e pode ser demonstrado com dados fictícios. Ele
> ainda não está autorizado para operação institucional: homologação formal,
> HTTPS, gestão de segredos, monitoramento, backup protegido e implantação na OCI
> permanecem pendentes.

## Visão geral

O projeto substitui formulários repetitivos e consultas manuais por um fluxo
rastreável de entrada e saída de veículos. O MVP atende quatro perfis:

| Perfil              | Responsabilidade atual                                    |
| ------------------- | --------------------------------------------------------- |
| Porteiro            | opera entradas, saídas e consultas necessárias à portaria |
| Vigilante           | assume o mesmo fluxo do Porteiro durante a substituição   |
| Setor de Transporte | supervisiona históricos e mantém frota e motoristas       |
| Administrador       | gerencia contas, eventos e operações técnicas autorizadas |

O sistema inclui acesso geral, acessos em aberto, histórico e correção auditada,
frota institucional, motoristas autorizados, utilizações, eventos, resumo diário,
contas e consulta restrita da auditoria. Câmeras, leitura automática de placas,
cancelas e fluxo de pedestres não pertencem ao MVP atual.

O estado confirmado, as hipóteses e as pendências estão na página
[Status do Projeto](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Status-do-Projeto).

## Arquitetura

```text
Navegador
   │
React + TypeScript + Nginx
   │ HTTP/API
ASP.NET Core (.NET 10)
   │
Application → Domain
   │
Infrastructure → EF Core → PostgreSQL 16
```

O monorepo usa um monólito modular com Clean Architecture adaptada. O domínio
não depende de ASP.NET Core, Entity Framework Core ou PostgreSQL; persistência e
migrations ficam em `Infrastructure`, e a API concentra o transporte HTTP e a
composição da aplicação.

| Área           | Tecnologias principais                                                                 |
| -------------- | -------------------------------------------------------------------------------------- |
| Frontend       | React 19, TypeScript, Vite, Tailwind CSS, React Router e Zod                           |
| Backend        | .NET 10 LTS, ASP.NET Core Minimal APIs e C#                                            |
| Dados          | Entity Framework Core 10, Npgsql e PostgreSQL 16                                       |
| Qualidade      | xUnit, Vitest, Testing Library, Testcontainers e axe-core                              |
| Infraestrutura | Docker, Compose, Nginx, GitHub Actions e GHCR                                          |
| Segurança      | RBAC no servidor, sessão renovável, auditoria, CodeQL, Trivy, SBOM, proveniência e ZAP |

## Início rápido com Docker

### 1. Pré-requisitos

- [Git](https://git-scm.com/downloads);
- [Docker Desktop](https://docs.docker.com/get-started/get-docker/) no Windows e
  macOS, ou Docker Engine com o plugin Compose no Linux;
- .NET SDK 10 e Node.js 24 LTS apenas para desenvolvimento fora dos containers.

Confirme a instalação:

```bash
docker --version
docker compose version
```

### 2. Preparar o ambiente local

Linux ou macOS:

```bash
cd infrastructure/docker
cp .env.example .env
```

PowerShell:

```powershell
Set-Location infrastructure/docker
Copy-Item .env.example .env
./New-DataProtectionCertificate.ps1
```

O `.env` é ignorado pelo Git. Os valores do exemplo são fictícios e exclusivos
para loopback; altere as senhas antes de compartilhar o ambiente. O gerador
grava o PFX e sua senha como dois arquivos em `secrets/`, diretório ignorado
pelo Git. Não envie nenhum deles por chat, issue ou commit.

### 3. Construir e iniciar

```bash
docker compose config --quiet
docker compose up --build --detach --wait
docker compose ps
```

| Serviço      | Endereço local                       |
| ------------ | ------------------------------------ |
| Frontend     | <http://localhost:3000>              |
| API          | <http://localhost:8080>              |
| Saúde da API | <http://localhost:8080/health/ready> |
| PostgreSQL   | `localhost:5432`                     |

O Compose não aplica migrations automaticamente. Na primeira execução, siga
[Migrations](#migrations) antes de usar os fluxos da aplicação. Para criar contas
e cenários exclusivamente fictícios, use o
[inicializador de demonstração](infrastructure/demo/README.md).

Para encerrar sem apagar o volume do banco:

```bash
docker compose down
```

> [!CAUTION]
> Não execute `docker compose down --volumes` se os dados locais precisarem ser
> preservados.

## Imagens versionadas

A release técnica publicada é `v0.3.0`. Os comandos pedidos para consumo humano
e para o Compose de implantação são:

```bash
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:0.3.0
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:0.3.0
```

As duas tags foram verificadas como manifestos para `linux/amd64` e
`linux/arm64`. Elas representam a release de 29 de setembro de 2026, não o estado
mais recente da branch `main`.

| Componente | Digest verificado da release `0.3.0`                                      |
| ---------- | ------------------------------------------------------------------------- |
| Backend    | `sha256:5c620d3c6609144c8cc0f53c1505dd3c2414d206aa29d69f96b6ab64b24d31b0` |
| Frontend   | `sha256:cd9ed896b47327a6ca71f191284e71988411ffc608b3d1e634d1a9df01b8ddc1` |

### Qual referência usar

| Referência       | Uso                                 | Exemplo            |
| ---------------- | ----------------------------------- | ------------------ |
| versão semântica | implantação de uma release revisada | `:0.3.0`           |
| commit           | rastreabilidade técnica da CI       | `:sha-<commit>`    |
| digest           | fixação criptográfica exata         | `@sha256:<digest>` |

Atestações assinadas de proveniência e SBOM são mantidas no serviço GitHub
Artifact Attestations, sem criar versões OCI `sha256-...` nos packages. Quando
for necessário fixar uma imagem por digest, a sintaxe correta usa `@sha256:`, e
não `:sha256-`.

Os packages principais recebem novas imagens somente em tags Git revisadas no
formato `vMAJOR.MINOR.PATCH`. Pushes comuns na `main` continuam sendo validados,
mas não substituem a versão exibida no GHCR. Cada nova entrega recebe uma nova
versão conforme Semantic Versioning; uma versão publicada nunca é movida para
outro conteúdo.

O Compose de produção não contém `build` e exige a mesma versão para frontend e
backend:

```bash
cd infrastructure/docker
cp .env.production.example .env.production

docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  config --quiet

docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  pull
```

Antes de executar `up`, leia o
[guia de implantação versionada](docs/operations/versioned-container-deployment.md).
Esse arquivo é uma base reproduzível; não representa uma produção institucional
já provisionada.

## Versões e releases

O projeto segue [Versionamento Semântico](https://semver.org/lang/pt-BR/) a
partir da primeira release publicada:

- `MAJOR`: mudança incompatível;
- `MINOR`: funcionalidade compatível adicionada;
- `PATCH`: correção compatível.

`v0.3.0` e as imagens `0.3.0` são imutáveis por política. Mudanças posteriores
ficam em [`Unreleased`](CHANGELOG.md#unreleased) até um Pull Request próprio de
release revisar o changelog, escolher a versão e criar a tag Git. Releases
anteriores, incluindo `v0.2.0`, permanecem disponíveis como histórico imutável.

Não confunda versões diferentes:

- a versão da release identifica o produto integrado;
- versões NuGet e npm identificam dependências;
- `version: 0.0.0` no `package.json` é metadado privado do pacote frontend e não
  identifica a release implantável;
- tags e digests das imagens identificam artefatos OCI.

Consulte o [`CHANGELOG.md`](CHANGELOG.md) e o
[processo de CI/CD](docs/development/ci-cd.md) antes de criar uma release.

## Desenvolvimento local

### Banco e configuração

Suba apenas o PostgreSQL:

```bash
cd infrastructure/docker
docker compose up --detach postgresql
```

Configure `ConnectionStrings__DefaultConnection` e
`Authentication__Jwt__SigningKey` somente no ambiente local. Não versione
credenciais nem reutilize os valores fictícios em ambiente compartilhado.

### Backend

```bash
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet run --project src/backend/ControleAcessoVeiculos.API --launch-profile http
```

A API de desenvolvimento usa <http://localhost:5118>. O documento OpenAPI fica
disponível apenas no ambiente `Development`.

### Frontend

```bash
cd src/frontend
npm ci
npm run dev
```

O Vite informa o endereço, normalmente <http://localhost:5173>, e encaminha
`/api` para a API local. Consulte o [guia do frontend](src/frontend/README.md)
para a estrutura, os fluxos e as validações específicas.

### Migrations

Instale ou atualize a ferramenta compatível com EF Core 10:

```bash
dotnet tool update --global dotnet-ef --version 10.*
```

Com a connection string local configurada, execute da raiz:

```bash
dotnet ef database update \
  --project src/backend/ControleAcessoVeiculos.Infrastructure \
  --startup-project src/backend/ControleAcessoVeiculos.API
```

Não use `EnsureCreated` e não aplique migrations automaticamente no startup da
API. Produção exige backup verificado, revisão da migration e execução única por
um operador autorizado.

## Validação

Backend:

```bash
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet test src/backend/ControleAcessoVeiculos.slnx --no-build --no-restore
dotnet format src/backend/ControleAcessoVeiculos.slnx --no-restore --verify-no-changes
```

Frontend:

```bash
cd src/frontend
npm ci
npm test
npm run lint
npm run build
npx prettier --check .
```

Containers:

```bash
docker compose -f infrastructure/docker/docker-compose.yml config --quiet
docker buildx imagetools inspect \
  ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:0.3.0
docker buildx imagetools inspect \
  ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:0.3.0
```

## Estrutura do repositório

```text
controle-acesso-veiculos/
├── .github/          # workflows, templates e instruções
├── docs/             # arquitetura, segurança, operação e validação
├── infrastructure/   # banco, dados fictícios, Dockerfiles e Compose
├── src/backend/      # API, Application, Domain, Infrastructure e testes
└── src/frontend/     # aplicação React e testes
```

## Segurança e operação

- não versione `.env`, tokens, chaves, senhas, dumps ou dados pessoais;
- use somente dados fictícios em demonstrações e evidências;
- autorização efetiva pertence à API, não à visibilidade de controles no frontend;
- JWT não é persistido no armazenamento web; a renovação usa cookie protegido,
  rotação, revogação e CSRF;
- imagens são analisadas com Trivy e publicadas com SBOM e proveniência;
- ZAP executa baseline passiva contra uma stack descartável;
- backup local com checksum é ensaio técnico, não backup protegido de produção.

Leia a [central de segurança](docs/security/README.md), a
[modelagem de ameaças](docs/security/threat-model.md) e o
[guia de desenvolvimento seguro](docs/security/secure-development-guide.md).

## Documentação

| Documento                                                                   | Finalidade                                          |
| --------------------------------------------------------------------------- | --------------------------------------------------- |
| [Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki)          | visão completa, requisitos, arc42, produto e status |
| [Prontidão da Unidade 1](docs/validation/unit-1-readiness.md)               | requisito acadêmico ligado à evidência              |
| [Homologação integrada](docs/validation/backend-mvp-homologation.md)        | roteiro dos quatro perfis                           |
| [CI/CD e containers](docs/development/ci-cd.md)                             | workflows, tags, scans, SBOM e proveniência         |
| [Implantação versionada](docs/operations/versioned-container-deployment.md) | pull, Compose, promoção e rollback                  |
| [Continuidade](docs/operations/data-retention-and-continuity.md)            | retenção, backup, RPO/RTO e contingência            |
| [Observabilidade](docs/operations/observability.md)                         | logs, métricas e traces                             |
| [Convenções de commit](docs/development/commit-conventions.md)              | branches, commits e Pull Requests                   |

Materiais acadêmicos complementares:

- [protótipo no Figma](https://www.figma.com/design/N6EOkXw8Ex7cZayyh4MJfY/Propotipagem?node-id=56-64&t=xA090z9jSE17HXUq-1);
- [slides semanais](https://canva.link/tjsp5iu5c5iwbdp).

## Contribuição

1. Abra ou confirme uma Issue com escopo e critérios de aceite.
2. Crie uma branch a partir da `main` atualizada.
3. Faça commits pequenos em inglês seguindo Conventional Commits.
4. Execute as validações proporcionais ao risco.
5. Abra um Pull Request com evidências, riscos e vínculo à Issue.
6. Aguarde revisão e checks obrigatórios antes do merge.

| Responsável                                         | Área principal                   | Apoio                       |
| --------------------------------------------------- | -------------------------------- | --------------------------- |
| [Raíssa Beatriz](https://github.com/Raissa-Beatriz) | frontend, UX/UI e acessibilidade | DevOps, infraestrutura e QA |
| [José Ernandes](https://github.com/ErnandesCosta)   | backend e banco de dados         | DevOps, infraestrutura e QA |

## Licença

Distribuído sob a [Apache License 2.0](LICENSE).
