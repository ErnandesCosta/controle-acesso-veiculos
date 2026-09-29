# Implantação com imagens versionadas

## Objetivo e limite

O arquivo `infrastructure/docker/docker-compose.production.yml` executa a mesma
versão publicada do frontend e do backend no GitHub Container Registry (GHCR).
Ele não contém `build`: o host de destino baixa os artefatos que a CI já
construiu, analisou e associou a uma release.

Essa configuração torna a implantação repetível, mas não significa que exista
um ambiente institucional de produção aprovado. Antes de uso real ainda são
obrigatórios servidor administrado, DNS, terminação HTTPS, gestão externa de
segredos, firewall, backup protegido, monitoramento e autorização institucional.

## Release disponível

A release técnica atual é `v0.2.0`. As duas imagens foram verificadas como
manifestos para `linux/amd64` e `linux/arm64`:

```bash
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:0.2.0
docker pull ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:0.2.0
```

A interface do GHCR também apresenta objetos internos com nomes derivados de
SHA-256. Eles suportam armazenamento e atestações OCI, mas não são o nome da
release. Para fixar conteúdo por digest, use
`imagem@sha256:<digest>`; para implantar a release revisada, use `imagem:0.2.0`.
Nunca mova uma versão publicada para outro digest: mudanças posteriores exigem
uma nova release.

## Pré-requisitos

- Docker Engine com o plugin Compose;
- acesso de leitura aos packages do GHCR, caso não sejam públicos;
- versão semântica já publicada para as duas imagens;
- banco com backup verificado e migrations compatíveis com a versão escolhida;
- proxy reverso com HTTPS no mesmo host ou em uma rede de confiança.

O Compose não publica portas do backend ou do PostgreSQL. O frontend fica, por
padrão, disponível somente em `127.0.0.1:8080`, para que um proxy HTTPS no host
seja o único ponto de entrada. Cookies de sessão são `Secure` no ambiente
`Production`; portanto, abrir essa porta diretamente por HTTP serve apenas para
diagnóstico de disponibilidade e não para autenticação.

## Preparação segura

Na pasta `infrastructure/docker`, crie um arquivo local a partir do exemplo:

```bash
cp .env.production.example .env.production
chmod 600 .env.production
```

No PowerShell:

```powershell
Set-Location infrastructure/docker
Copy-Item .env.production.example .env.production
```

Defina no arquivo local:

- `DEPLOYMENT_VERSION`: a mesma release para frontend e backend, como `0.2.0`;
- `ALLOWED_HOSTS`: o domínio público esperado pela API e `localhost`, usado
  somente pela verificação de saúde interna;
- credenciais próprias do PostgreSQL; prefira senha aleatória hexadecimal ou
  base64url, sem delimitadores de connection string;
- uma chave JWT aleatória com pelo menos 32 bytes;
- endereço e porta de loopback que o proxy HTTPS acessará.

O `.env.production` é ignorado pelo Git. Ele ainda é apenas uma alternativa de
implantação simples: no ambiente definitivo, prefira um secret manager ou
Docker Secrets, restrinja a leitura ao operador e nunca envie seu conteúdo por
issue, Pull Request, chat ou log.

Se os packages forem privados, autentique o Docker com uma credencial pessoal
de escopo mínimo `read:packages`. Não armazene o token em arquivo do projeto:

```bash
gh auth token | docker login ghcr.io --username SEU_USUARIO --password-stdin
```

## Implantação

Os exemplos seguintes devem ser executados em `infrastructure/docker`:

```bash
docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  config --quiet

docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  pull
```

O `pull` deve resolver duas imagens com a mesma versão:

```text
ghcr.io/ifpebj-ti/controle-acesso-veiculos-backend:<versão>
ghcr.io/ifpebj-ti/controle-acesso-veiculos-frontend:<versão>
```

Registre os digests resolvidos no histórico da implantação. Para uma implantação
institucional, verifique também as atestações conforme
[`docs/development/ci-cd.md`](../development/ci-cd.md).

O Compose não aplica migrations automaticamente. Isso evita que toda réplica da
API altere o schema ao iniciar. Antes da promoção, um operador autorizado deve:

1. criar e verificar um backup;
2. revisar as migrations da mesma tag Git da imagem;
3. aplicá-las uma única vez por um caminho administrativo protegido;
4. confirmar a compatibilidade de rollback.

Depois dessa etapa controlada, inicie os serviços:

```bash
docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  up --detach --remove-orphans --wait

docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  ps
```

O resultado esperado é PostgreSQL, backend e frontend saudáveis. A validação
funcional deve usar o domínio HTTPS, não a porta HTTP de loopback.

## Atualização e rollback

Para atualizar, altere somente `DEPLOYMENT_VERSION` para uma release revisada,
faça backup, aplique as migrations controladas e execute novamente `pull` e
`up --detach --wait`. Não use `main` ou `latest` como versão de implantação.

Para rollback da aplicação, restaure a versão anterior no arquivo local e
execute novamente os mesmos comandos. Se a release nova alterou o schema, não
faça downgrade no escuro: siga a estratégia revisada para aquela migration ou
restaure banco e aplicação a partir do mesmo ponto de recuperação.

Para parar a aplicação sem apagar os dados:

```bash
docker compose \
  --env-file .env.production \
  --file docker-compose.production.yml \
  down
```

Não acrescente `--volumes` em ambiente com dados que precisem ser preservados.
Consulte também o
[`procedimento de continuidade`](data-retention-and-continuity.md).
