# CI/CD e segurança de containers

## Estado

Esta página documenta a fundação de integração contínua iniciada na Issue #25 e
ampliada pelas Issues #90, #104, #218, #227, #243 e #318. Os workflows validam
código e imagens em Pull Requests e na `main`, mas os packages principais do
GitHub Container Registry recebem novas imagens somente por uma tag revisada no
formato `vMAJOR.MINOR.PATCH`. Frontend e backend são verificados para
`linux/amd64` e `linux/arm64`; cada digest publicado recebe proveniência assinada
e um SBOM SPDX 2.3 de cada arquitetura. A publicação no registry não realiza
deploy nem torna o sistema pronto para produção.

O Compose local continua responsável por construir e testar o código da árvore
de trabalho. Para implantação, o
[`Compose com imagens versionadas`](../operations/versioned-container-deployment.md)
consome a mesma tag semântica de frontend e backend no GHCR, sem reconstrução no
host. A CI valida que essa configuração não introduz `build` nem publica as
portas da API ou do PostgreSQL.

## Workflows

| Workflow               | Gatilho                                                                   | Verificações                                                                                                                                                                                                     |
| ---------------------- | ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CI - Backend           | Alterações do backend e de suas regras de formato                         | restore, `dotnet format`, build Release com warnings como erros, suíte automatizada e cobertura                                                                                                                  |
| CI - Frontend          | Alterações do frontend                                                    | `npm ci`, ESLint e build Vite                                                                                                                                                                                    |
| CI - Containers        | Código, Dockerfiles, Compose, contexto Docker ou tag `vMAJOR.MINOR.PATCH` | build isolado, Trivy e SBOM de frontend e backend em `linux/amd64` e `linux/arm64`; smoke test integrado e baseline DAST passiva com OWASP ZAP; publicação, atestação e tag semântica somente em tags de release |
| CI - Database recovery | Scripts de backup ou configuração local do PostgreSQL                     | dump lógico, manifesto SHA-256, restauração completa em banco isolado, rejeição de adulteração e limpeza dos recursos temporários                                                                                |
| Dependency Review      | Toda Pull Request                                                         | bloqueio de novas dependências com vulnerabilidade alta ou crítica                                                                                                                                               |

Todas as actions de terceiros estão fixadas por SHA de commit e acompanhadas do
número da release auditada. Os jobs de validação usam apenas `contents: read`. O
job de publicação, restrito a push de tag semântica, acrescenta `packages: write`,
`id-token: write` e `attestations: write`. Somente o job que publica o manifesto
também recebe `artifact-metadata: write`, necessário para a action oficial
registrar onde o artefato atestado está armazenado. O token OIDC é efêmero e
usado para assinar a atestação; Pull Requests não recebem essas permissões.
Todos os workflows cancelam execuções obsoletas da mesma referência e possuem
timeout.

Os resultados TRX e Cobertura do backend são mantidos por 14 dias. Cobertura é evidência de apoio; não substitui revisão de cenários, risco e qualidade dos testes.

## Dependabot

O Dependabot verifica semanalmente:

- pacotes NuGet do backend;
- pacotes npm do frontend;
- imagens base dos Dockerfiles de backend e frontend;
- GitHub Actions.

As atualizações continuam sujeitas aos mesmos testes e à mesma revisão de uma Pull Request comum. Dependabot não autoriza merge automático.

Atualizações major do toolchain frontend são deliberadas: propostas para Node 26 e TypeScript 7 continuam visíveis, mas só devem ser mescladas depois de cumprirem os critérios de compatibilidade e LTS da Issue #41. O projeto não suprime alertas de novas versões ou correções de segurança.

## Controles dos containers

- O backend utiliza o usuário não privilegiado `app` fornecido pela imagem oficial .NET.
- O frontend utiliza a imagem Nginx unprivileged e escuta na porta interna 8080.
- Backend e frontend usam filesystem raiz somente leitura, `/tmp` temporário, `no-new-privileges` e remoção de capabilities Linux.
- O frontend instala dependências com `npm ci` e o lockfile versionado.
- O `.dockerignore` impede o envio de `.env`, segredos, metadados Git, dependências locais, resultados de teste e documentação para o contexto de build.

## Liveness e readiness

| Endpoint            | Finalidade                                     | Dependências           |
| ------------------- | ---------------------------------------------- | ---------------------- |
| `GET /health`       | Alias compatível de liveness                   | Nenhuma                |
| `GET /health/live`  | Confirmar que o processo HTTP responde         | Nenhuma                |
| `GET /health/ready` | Confirmar que a instância pode receber tráfego | Conexão com PostgreSQL |

Readiness retorna `503 Service Unavailable` quando o banco não pode ser acessado. A resposta expõe apenas `Healthy` ou `Unhealthy` e timestamp; exceções e detalhes da conexão não são retornados.

## Smoke test integrado do Compose

O job `Run integrated Compose smoke test` valida uma propriedade diferente dos
builds isolados: confirma que o arquivo Compose versionado consegue iniciar
PostgreSQL, API e frontend em conjunto. O job:

1. gera senha de banco e chave JWT efêmeras, mascara os valores e os mantém
   somente no ambiente do runner;
2. usa um nome de projeto exclusivo da execução e solicita portas aleatórias ao
   Docker, evitando colisões com outras stacks;
3. valida a configuração sem imprimir os valores resolvidos;
4. constrói e inicia os três serviços;
5. exige resposta `Healthy` de `/health/ready` e o HTML esperado do frontend;
6. mostra estado e logs somente quando há falha;
7. remove containers, rede e volume descartáveis mesmo após erro.

O job não aplica migrations, não cria usuário, não usa dados institucionais e não
substitui testes funcionais ou homologação. A publicação no GHCR depende do smoke
test, impedindo a distribuição automática de uma revisão cuja stack integrada
não inicia.

## Análise dinâmica passiva

Depois da readiness e das verificações de cabeçalhos, o mesmo ambiente
descartável recebe o OWASP ZAP Baseline Scan. O scanner entra apenas na rede do
Compose, acessa o frontend pelo nome interno e executa spider tradicional e
regras passivas; nenhum Active Scan é usado.

A imagem oficial ZAP 2.17.0 está fixada por digest. A política em
`infrastructure/security/zap-baseline.conf` reprova achados de segurança
classificados como `FAIL`, mantém hipóteses e informações em `WARN` e trata erro
do scanner ou relatório ausente como falha. HTML, JSON e Markdown são anexados à
execução por 14 dias. A classificação, baseline e limites estão documentados em
[`dynamic-application-security-testing.md`](../security/dynamic-application-security-testing.md).

## Análise de imagens

Em Pull Requests e branches de trabalho, cada imagem é construída separadamente
para `linux/amd64` e `linux/arm64`, sem `push`, e carregada apenas no runner. O
QEMU é habilitado somente para a variante ARM64. Caches e tags locais são
isolados por imagem e arquitetura para impedir reutilização cruzada indevida.

O Trivy aplica duas barreiras complementares a cada variante:

- qualquer vulnerabilidade `CRITICAL` bloqueia o pipeline, ainda que não exista
  correção publicada;
- todas as vulnerabilidades `HIGH` são exibidas; aquelas com correção disponível
  também bloqueiam o pipeline, enquanto as demais exigem revisão de
  explorabilidade, troca da base, mitigação ou aceitação formal de risco.

Em Pull Requests e na `main`, cada variante é construída, analisada e recebe seu
SBOM sem autenticação nem push. Uma tag de release repete essas barreiras antes
da autenticação no registry. Somente então as variantes aprovadas são enviadas
com tags imutáveis por commit e arquitetura e reunidas no manifesto
`sha-<commit>`.

## Publicação em registry

Uma tag Git revisada no formato `vMAJOR.MINOR.PATCH` publica:

| Componente | Imagem                                                |
| ---------- | ----------------------------------------------------- |
| Backend    | `ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend`  |
| Frontend   | `ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend` |

O pipeline primeiro valida a tag, reconstrói e analisa as quatro variantes,
executa o smoke test e publica o manifesto imutável `sha-<commit>` com exatamente
AMD64 e ARM64. Em seguida, associa ao digest sua proveniência e os dois SBOMs.
Somente depois dessas atestações aplica `MAJOR.MINOR.PATCH` ao mesmo digest. Essa
ordem faz da versão semântica a última referência legível publicada. As
atestações ficam no serviço GitHub Artifact Attestations e não criam versões OCI
`sha256-...` no GHCR.

Por exemplo, a tag Git `v0.3.0` publicará `0.3.0` para backend e frontend. O
workflow recusa mover uma versão existente para outro digest, não cria `latest`
e não atualiza `main`. Um push comum na `main` valida o candidato, mas não altera
os packages principais.

Cada release também mantém `sha-<commit>-amd64` e `sha-<commit>-arm64` como
referências imutáveis das variantes efetivamente analisadas. Elas formam o
manifesto e permitem auditoria por arquitetura; consumidores normais devem usar
`sha-<commit>` ou seu digest.

Exemplo de download dos dois componentes associados à release atual:

```bash
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:0.2.0
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:0.2.0
```

Para fixar exatamente o conteúdo, use o digest com `@`, e não como se fosse uma
tag:

```bash
docker pull \
  ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend@sha256:<digest>
```

Exemplo de download da imagem rastreável por commit:

```bash
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:sha-<commit>
```

O workflow não publica atestações como versões OCI `sha256-...` no package. A
referência imutável por conteúdo usa `@sha256:<digest>`; a versão operacional
revisada usa `:MAJOR.MINOR.PATCH`.

Confirme as plataformas declaradas sem executar a imagem:

```bash
docker buildx imagetools inspect \
  ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:sha-<commit>
```

O resultado deve listar `linux/amd64` e `linux/arm64`. Para exercitar uma
variante explicitamente em host compatível ou com emulação configurada:

```bash
docker pull --platform linux/arm64 \
  ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:sha-<commit>
```

O workflow usa o `GITHUB_TOKEN` efêmero do job e autentica somente depois do
scan. Pull Requests não executam o job de publicação e permanecem sem
`packages: write`. Nenhum token de registry deve ser adicionado ao repositório.

A tag Git e a GitHub Release são criadas somente depois que o Pull Request do
release é revisado e integrado à `main`. A execução da tag deve terminar com os
dois manifestos aprovados antes da publicação da Release no GitHub. Uma release
do MVP não representa homologação institucional nem autorização de deploy.

Depois de montar e validar exatamente as plataformas `linux/amd64` e
`linux/arm64`, o workflow resolve o digest da tag `sha-<commit>`, exige o formato
`sha256:<64 caracteres hexadecimais>` e usa `actions/attest` fixada por SHA para
gerar proveniência SLSA assinada. A atestação é associada ao repositório e ao
digest no serviço GitHub Artifact Attestations, sem ser anexada como referrer OCI
no GHCR. A versão semântica facilita a operação; a evidência imutável continua
sendo a tag por commit ou o digest.

Antes de cada push, o Trivy também gera um SBOM SPDX 2.3 JSON da variante já
aprovada pelo scan. O workflow rejeita arquivo vazio, documento sem pacotes,
versão SPDX inesperada ou tamanho superior ao limite de 16 MiB aceito pela
action. Depois de montar o manifesto, `actions/attest` vincula os documentos de
AMD64 e ARM64 ao mesmo nome e digest imutável da proveniência e os registra no
serviço de atestações do GitHub. Em Pull Requests, os arquivos são gerados e
validados apenas nos runners descartáveis; nenhuma atestação ou imagem é
publicada.

Após autenticar no GHCR, verifique uma imagem com GitHub CLI:

```bash
gh attestation verify \
  oci://ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:sha-<commit> \
  --repo ifpebj-ti/controle-acesso-veiculos
```

Repita para `controle-acesso-veiculos-frontend`. A verificação confirma digest,
assinatura e identidade do repositório produtor; não substitui análise de
vulnerabilidades, revisão do Dockerfile ou política de admissão no ambiente.

Para verificar e extrair o SBOM SPDX atestado:

```bash
gh attestation verify \
  oci://ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:sha-<commit> \
  --repo ifpebj-ti/controle-acesso-veiculos \
  --signer-workflow ifpebj-ti/controle-acesso-veiculos/.github/workflows/ci-containers.yml \
  --predicate-type https://spdx.dev/Document \
  --format json \
  --jq '.[].verificationResult.statement.predicate' \
  > backend.spdx.json
```

O comando deve ser repetido para o frontend. O JSON extraído permite inventário
e investigação, mas não deve ser interpretado isoladamente como garantia de que
os componentes estão livres de vulnerabilidades.

O primeiro push cria os packages no escopo da organização. A visibilidade do
package é uma decisão administrativa: se a equipe precisar permitir download
anônimo para apresentação acadêmica, um responsável da organização deverá
alterá-la para pública nas configurações do package. Enquanto o package for
privado, o download local exige autenticação no GHCR com permissão
`read:packages`.

Publicar uma imagem não promove a aplicação para homologação ou produção. Antes
de qualquer deploy real, a equipe ainda precisa definir:

- ambiente e responsável pela implantação;
- política de retenção das imagens;
- política de admissão que exija proveniência e SBOM válidos no ambiente de destino;
- aprovação de promoção entre desenvolvimento, homologação e produção;
- estratégia de rollback e resposta a vulnerabilidades.

### Imagem de runtime do frontend

O frontend usa a variante oficial `stable-alpine-slim` do NGINX sem privilégios,
fixada pelo digest do manifesto multiplataforma. A variante reduzida é suficiente
para servir os arquivos estáticos e evita ferramentas de uso geral que não são
necessárias em runtime e ampliam a superfície de vulnerabilidades.

O `apk upgrade --no-cache` permanece como defesa adicional. A atualização semanal
do digest é monitorada pelo Dependabot; quando o digest da base muda, o cache da
camada de atualização é invalidado. Toda alteração da base continua sujeita ao
build, ao Trivy, à geração do SBOM e ao smoke test integrado antes do merge.

## Operação e revisão

Antes do merge:

1. Confirmar que todos os checks foram executados.
2. Revisar alertas do Trivy e do Dependency Review.
3. Revisar alertas e relatórios do OWASP ZAP quando o job integrado executar.
4. Confirmar que nenhum segredo apareceu no diff ou nos logs.
5. Verificar que alterações de dependência possuem justificativa.
6. Registrar exceções e riscos residuais na Pull Request.

## Referências oficiais

- [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [.NET container images and non-root user](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images#non-root-user)
- [GitHub Actions workflow permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions)
- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations)
- [Dependabot options](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)
- [NGINX unprivileged image](https://github.com/nginx/docker-nginx-unprivileged)
- [Trivy Action](https://github.com/aquasecurity/trivy-action)
- [OWASP ZAP Baseline Scan](https://www.zaproxy.org/docs/docker/baseline-scan/)
