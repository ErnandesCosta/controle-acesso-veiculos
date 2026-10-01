# ADR 0004 — Topologia segura e econômica da homologação na OCI

- **Status:** Proposta; depende do preflight da tenancy e da revisão de custo
- **Data:** 1º de outubro de 2026
- **Issue:** #391
- **Issue de implantação:** #382

## Contexto

A segunda unidade prevê disponibilizar o MVP para um piloto acompanhado no IFPE,
com dados inicialmente fictícios, observação dos usuários, registro de defeitos e
medição de consumo. Esse ambiente é de homologação: ele não equivale à produção
institucional nem autoriza, por si só, o tratamento de dados pessoais reais.

O repositório já publica imagens `linux/amd64` e `linux/arm64` versionadas no GHCR
e possui um Compose de produção que mantém backend e PostgreSQL sem portas
publicadas. O frontend escuta apenas no loopback do host, esperando um terminador
HTTPS externo. A base de backup protegido está versionada, mas ainda não foi
provisionada nem teve restauração comprovada na OCI.

Foi informado um crédito aproximado de US$ 300 para um ano. Esse dado pertence à
conta fornecida pelo curso e não deve ser confundido com a oferta pública padrão do
OCI Free Tier. Saldo, validade, moeda, região, limites, capacidade e serviços
elegíveis precisam ser conferidos no Console antes de qualquer criação de recurso.
Orçamentos da OCI geram alertas; eles não funcionam como bloqueio automático de
cobrança.

## Direção proposta

Adotar uma implantação **progressiva, de nó único e reversível** para o primeiro
piloto. O Terraform da Issue #382 só poderá ser finalizado depois do preflight
descrito nesta ADR.

### Topologia inicial

- um compartimento exclusivo para homologação, com tags de custo e quotas;
- uma única VM flexível, preferencialmente Arm quando capacidade, compatibilidade e
  preço forem confirmados na região escolhida;
- volume de inicialização e volumes persistentes dimensionados a partir de medição,
  sem superdimensionamento preventivo;
- VCN, subnet, Network Security Group e regras de saída explicitamente gerenciados
  por Terraform;
- somente HTTPS na porta 443 acessível aos usuários;
- frontend, API e PostgreSQL executados pelo Compose versionado no mesmo host;
- proxy HTTPS no host encaminhando apenas para o frontend em
  `127.0.0.1:8080`;
- backend, PostgreSQL, Docker daemon, métricas internas e interfaces administrativas
  sem exposição pública;
- acesso administrativo preferencial por sessão temporária e auditável do OCI
  Bastion, usando a interface privada da VM e identidade individual com MFA;
- nenhuma regra SSH permanente para `0.0.0.0/0`; uma alternativa emergencial deve
  ser temporária, limitada a endereço conhecido, aprovada e removida após o uso;
- imagens do frontend e backend baixadas do GHCR pela mesma versão semântica, sem
  build no host;
- dados e key ring persistentes fora dos containers; certificado, senhas e chaves
  fornecidos por mecanismo de segredos, nunca por Git, imagem ou `cloud-init`;
- backup protegido da Issue #311 e restauração isolada comprovados antes de dados
  que precisem ser preservados.

Uma VM única é intencional para a homologação econômica e constitui ponto único de
falha conhecido. Alta disponibilidade, banco gerenciado e balanceador não serão
adicionados antes de existir necessidade medida, orçamento aprovado e estratégia de
operação correspondente.

### Publicação e operação

O DNS deve apontar um domínio ou subdomínio institucional para o endpoint aprovado.
O certificado precisa ser emitido e renovado automaticamente por um mecanismo que
não grave credenciais no repositório. A aplicação não será apresentada aos usuários
por HTTP nem pelo IP e porta internos.

Cada implantação deverá registrar versão, digests resolvidos, migrations aplicadas,
responsável e resultado dos health checks. Atualização e rollback usarão releases
imutáveis; `main` e `latest` não são versões de implantação. O host não executará
compilação, e a migração do banco será uma etapa administrativa única precedida por
backup verificável.

## Gate de preflight

Antes de executar `terraform plan` contra a tenancy, uma pessoa autorizada deve
confirmar e registrar de forma sanitizada:

1. tipo de assinatura, saldo, moeda e data de expiração do crédito;
2. região principal, domínios de disponibilidade e capacidade do shape candidato;
3. limites de serviço e permissões disponíveis para compartments, IAM, quotas,
   budgets, networking, compute, Vault e Object Storage;
4. custo mensal e anual estimado para compute, volumes, endereço público, tráfego,
   DNS, Vault, Object Storage, logs, métricas e eventuais gateways;
5. destinatários institucionais dos alertas financeiros e operacionais;
6. domínio ou subdomínio, responsável por DNS e método de renovação do certificado;
7. identidades humanas individuais com MFA e grupos IAM de menor privilégio;
8. janela do piloto, rotina de acompanhamento e data de desligamento;
9. autorização explícita antes do primeiro `apply`.

Valores de OCID, endereços, e-mails, chaves, tokens e detalhes exploráveis não devem
ser publicados em Issue, Pull Request, Wiki, chat ou logs. O plano deve ser revisado
por outra pessoa autorizada e armazenado apenas como evidência sanitizada.

## Alternativas avaliadas

### VM pública endurecida — proposta para o piloto

É a alternativa de menor complexidade e tende a reduzir serviços cobrados. A VM
pode ter endereço público, mas o NSG aceita somente 443 para a aplicação. Acesso
administrativo ocorre pelo caminho temporário aprovado, não por SSH público
permanente. O risco residual é a exposição direta do endpoint da VM e a existência
de um único host.

### VM privada atrás de load balancer, NAT e Bastion

Oferece separação de rede mais forte e um endpoint público desacoplado do host, mas
adiciona serviços, custo e complexidade operacional. É a evolução preferível se o
preflight confirmar orçamento, necessidade e capacidade de manutenção. Não será
adotada apenas para aparentar arquitetura de produção em um piloto acadêmico.

### Serviços gerenciados e alta disponibilidade

Banco gerenciado, múltiplas VMs, múltiplos domínios de disponibilidade e
orquestração foram adiados. Eles elevam custo e carga operacional antes de haver
medidas de disponibilidade, volume ou concorrência que os justifiquem.

### Exposição direta de HTTP, PostgreSQL ou SSH irrestrito

Rejeitada. Cookies de produção exigem HTTPS, o banco não é uma interface pública e
uma porta administrativa global aumenta desnecessariamente a superfície de ataque.

## Consequências

### Positivas

- o primeiro piloto permanece simples, reproduzível e financeiramente observável;
- as mesmas imagens revisadas são usadas em desenvolvimento de release e
  homologação;
- a superfície pública fica limitada ao serviço necessário ao usuário;
- decisões de custo e segurança são verificadas antes de criar recursos;
- a topologia pode evoluir sem confundir homologação com produção.

### Limitações e riscos aceitos

- falha da VM interrompe todo o ambiente até recuperação;
- capacidade Arm e benefícios gratuitos não são garantidos;
- manter aplicação, API e banco no mesmo host aumenta o impacto de falha ou
  comprometimento;
- alertas de orçamento podem ocorrer depois do consumo e não impedem cobrança;
- a alternativa econômica exige disciplina de patch, backup, monitoramento e
  desligamento de recursos esquecidos;
- a decisão permanece proposta até o preflight e a revisão humana da Issue #382.

## Critérios para evolução

A topologia deverá ser reavaliada se o piloto demonstrar indisponibilidade
inaceitável, concorrência acima da capacidade medida, necessidade de manutenção sem
interrupção, isolamento adicional, exigência institucional ou orçamento suficiente
para serviços gerenciados. A mudança exige nova ADR e não deve ser feita por ajuste
manual não rastreado.

## Rastreabilidade

- Issue #382 — implantação do ambiente de homologação;
- Issue #311 — backup protegido na OCI;
- Issue #312 — recuperação pontual do PostgreSQL;
- ADR 0002 — retenção de dados e backup protegido;
- ADR 0003 — key ring persistente e protegido;
- `infrastructure/docker/docker-compose.production.yml` — stack versionada;
- `docs/operations/versioned-container-deployment.md` — implantação e rollback.

## Referências

- [OCI Bastion](https://docs.oracle.com/en-us/iaas/Content/Bastion/Concepts/bastionoverview.htm)
- [OCI Budgets](https://docs.oracle.com/en-us/iaas/Content/Billing/Concepts/budgetsoverview.htm)
- [OCI Compartment Quotas](https://docs.oracle.com/en-us/iaas/Content/Quotas/home.htm)
- [OCI Free Tier](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier.htm)
- [OCI Compute Shapes](https://docs.oracle.com/en-us/iaas/Content/Compute/References/computeshapes.htm)
