# Evidências da Unidade 1

## Objetivo

Este documento relaciona o marco acadêmico da Unidade 1 às evidências
versionadas do projeto. A fotografia foi verificada em **29 de setembro de 2026**
sobre a `main` após o merge do [PR #304](https://github.com/ifpebj-ti/controle-acesso-veiculos/pull/304)
e deve ser conferida novamente antes da apresentação.

As classificações usadas são:

- **Atendido:** existe evidência verificável para o requisito;
- **Ampliado:** o requisito foi atendido e recebeu controles adicionais;
- **Pendente institucional:** depende de decisão, infraestrutura ou aceitação
  externa e não invalida a entrega técnica da Unidade 1.

## Engenharia de Software

| Entrega exigida                            | Evidência                                                                                                                                                                                                                                                                                                                    | Estado                                                          |
| ------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| Documento de Visão do Projeto              | [Visão do Projeto na Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Visao-do-Projeto)                                                                                                                                                                                                                      | Atendido                                                        |
| Análise de Concorrência                    | [Análise de Concorrência na Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Analise-de-Concorrencia)                                                                                                                                                                                                        | Atendido                                                        |
| Backlog do produto e requisitos no Project | [Requisitos e Backlog](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Requisitos-e-Backlog), [Issues](https://github.com/ifpebj-ti/controle-acesso-veiculos/issues) e [Projects da organização](https://github.com/orgs/ifpebj-ti/projects)                                                                      | Atendido; o Project pode exigir acesso à organização            |
| Arquitetura e modelagem de dados           | [Arquitetura arc42](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Arquitetura-de-Software), [Modelo de Dados](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Modelo-de-Dados), [backend](../../src/backend) e [migrations](../../src/backend/ControleAcessoVeiculos.Infrastructure/Data/Migrations) | Ampliado com arquitetura implementada, constraints e migrations |

## Segurança

| Entrega exigida                                         | Evidência                                                                                                                                                                                                                                      | Estado                                                                                     |
| ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Modelagem de ameaças                                    | [Modelo STRIDE](../security/threat-model.md), [arquivo do OWASP Threat Dragon](../security/controle-acesso-veiculos-threat-model.json) e [visão na Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Modelagem-de-Amea%C3%A7as) | Ampliado com riscos, controles, responsáveis e risco residual                              |
| Guia de boas práticas de desenvolvimento seguro         | [Guia versionado](../security/secure-development-guide.md) e [guia na Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Guia-de-Boas-Praticas-de-Desenvolvimento-Seguro)                                                        | Atendido                                                                                   |
| Zero vulnerabilidades críticas aferidas pelo Dependabot | [Dependabot](https://github.com/ifpebj-ti/controle-acesso-veiculos/security/dependabot) e [Dependency Review](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/workflows/dependency-review.yml)                                   | Atendido na verificação de 29/09/2026: nenhum alerta aberto; isso não significa risco zero |

Controles adicionais incluem CodeQL, Trivy para imagens, SBOM SPDX,
proveniência assinada, segredos detectados pelo GitHub e baseline passiva do
OWASP ZAP. O [guia de DAST](../security/dynamic-application-security-testing.md)
explica o alcance e as limitações do ZAP.

A recuperação administrativa do MVP possui dois Administradores funcionais e
entrega direta da credencial temporária ao titular, conforme a Issue #220. A
observação desse procedimento no ambiente real continua na Issue #162 e não
representa liberação para produção.

## Infraestrutura

| Entrega exigida                           | Evidência                                                                                                                                                                                                                                                                                            | Estado                                                                                                          |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Aplicação conteinerizada                  | [Dockerfiles e arquivos Compose](../../infrastructure/docker)                                                                                                                                                                                                                                        | Atendido para desenvolvimento e demonstração                                                                    |
| Guia de execução, configuração e operação | [README](../../README.md), [deploy versionado](../operations/versioned-container-deployment.md), [banco](../../infrastructure/database/README.md) e [guia na Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki/Guia-de-Execucao-Configuracao-Deploy-e-Operacao)                       | Ampliado; operação institucional ainda pendente                                                                 |
| Início da esteira CI/CD                   | [CI backend](../../.github/workflows/ci-backend.yml), [CI frontend](../../.github/workflows/ci-frontend.yml), [containers](../../.github/workflows/ci-containers.yml), [recuperação](../../.github/workflows/ci-database-recovery.yml) e [dados fictícios](../../.github/workflows/ci-demo-data.yml) | Ampliado com lint, build, testes, cobertura, scan, registry, AMD64/ARM64, smoke test, DAST, SBOM e proveniência |

## Distribuição versionada solicitada pelo professor

O projeto possui o arquivo
[`docker-compose.production.yml`](../../infrastructure/docker/docker-compose.production.yml),
que não contém diretivas `build`. Ele exige `DEPLOYMENT_VERSION` e consome a
mesma versão do frontend e do backend no GHCR.

A release técnica [`v0.2.0`](https://github.com/ifpebj-ti/controle-acesso-veiculos/releases/tag/v0.2.0)
está registrada no [`CHANGELOG.md`](../../CHANGELOG.md). As imagens podem ser
obtidas pelos nomes semânticos solicitados:

```bash
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:0.2.0
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:0.2.0
```

A release registra também os digests imutáveis correspondentes. Assim, a tag
semântica facilita a operação e o digest permite conferir exatamente o artefato
executado.

## Evidência quantitativa da baseline

| Item na `main`             | Evidência em 29/09/2026                            |
| -------------------------- | -------------------------------------------------- |
| Testes de backend          | 254 aprovados, incluindo integração com PostgreSQL |
| Testes de frontend         | 432 aprovados em 44 arquivos                       |
| Migrations EF Core         | 16 migrations versionadas                          |
| Alertas Dependabot abertos | 0                                                  |
| Alertas CodeQL abertos     | 0                                                  |
| Release técnica            | `v0.2.0`                                           |

Os números são evidências datadas, não metas fixas. A fonte vigente para
cada execução é o [GitHub Actions](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions).

## Conclusão

Pelas evidências versionadas, **todos os itens exigidos no marco da Unidade 1
estão atendidos**, e a esteira de segurança e distribuição supera o mínimo
solicitado.

Essa conclusão acadêmica não equivale a afirmar que o sistema está pronto para
produção. Permanecem fora desse aceite: infraestrutura institucional, HTTPS,
gestão externa de segredos, monitoramento operado, backup externo, retenção,
contingência e homologação final dos fluxos no dispositivo de destino.

## Roteiro curto de demonstração

1. mostrar a visão, concorrência, backlog, arc42 e modelo de dados na Wiki;
2. abrir o modelo STRIDE no Threat Dragon e explicar uma ameaça, seu controle e
   o risco residual;
3. mostrar os workflows aprovados e o estado atual do Dependabot;
4. abrir a release `v0.2.0`, o changelog e as imagens `:0.2.0`;
5. mostrar que o Compose de produção usa `image`, não `build`;
6. encerrar diferenciando MVP técnico, homologação e produção.
