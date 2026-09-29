# ADR 0002 — Retenção de dados e backup protegido na OCI

## Status

Aceita como direção de arquitetura em 29 de setembro de 2026. A implantação na
OCI e a comprovação das metas permanecem pendentes nas Issues #311 e #312.

## Contexto

O MVP substitui planilhas físicas por registros digitais de circulação de
veículos. O PostgreSQL já possui dump lógico, manifesto SHA-256 e restauração
isolada exercitada na CI, mas o procedimento local não oferece armazenamento
externo, confidencialidade, imutabilidade ou recuperação pontual.

Eurico, responsável funcional pelo processo no Setor de Transporte, definiu
cinco anos como prazo operacional para os registros e para os formulários de
contingência. A conta OCI disponibilizada ao projeto será o ambiente previsto
para homologação e futura produção. A equipe do curso de Engenharia de Software
manterá tecnicamente o sistema, acompanhada pelos orientadores.

## Decisão

### Retenção

- acessos gerais, usos institucionais, eventos, auditorias e formulários de
  contingência reconciliados: cinco anos após o encerramento;
- pessoas, veículos e identidade mínima de contas: enquanto ativos ou necessários
  à integridade de registros ainda retidos;
- logs técnicos: 90 dias, salvo preservação documentada por incidente;
- backups: janela móvel de 35 dias, porque backup não é arquivo histórico;
- descarte automático somente será implementado após revisão arquivística ou da
  referência institucional de proteção de dados.

### Continuidade

- RPO-alvo de produção: até uma hora;
- RTO-alvo de produção: até quatro horas durante o período com suporte;
- a portaria entra imediatamente em contingência no papel;
- o dump lógico diário permanece como caminho portátil adicional, mas entrega
  apenas RPO técnico de até 24 horas;
- o RPO de uma hora exige backup físico e arquivamento contínuo de WAL/PITR;
- o primeiro exercício institucional ocorrerá na segunda unidade e bloqueará a
  declaração de prontidão para produção até ser aprovado.

### Proteção na OCI

- Object Storage privado em compartimento dedicado;
- regra temporal imutável de 35 dias, ensaiada antes do bloqueio irreversível;
- lifecycle para exclusão após a janela protegida;
- criptografia com chave gerenciada no OCI Vault;
- VM autenticada por instance principal, sem chave de usuário no host;
- grupos IAM distintos para upload, recuperação, custódia de chaves e auditoria;
- contas humanas individuais com MFA e menor privilégio;
- revisão de acesso a cada transição de equipe ou semestre;
- restauração somente em destino isolado por padrão.

Não será habilitado versionamento no bucket de retenção porque a OCI não permite
versionamento e regra de retenção ativa simultaneamente. Dumps e manifestos usam
nomes únicos, evitando sobrescrita legítima.

### Custódia

Os desenvolvedores e orientadores não receberão arquivos com chave criptográfica.
“Custódia” significa participação em grupos IAM controlados que autorizam o uso
do Vault. Os orientadores aprovam acessos privilegiados e a recuperação; a VM
realiza uploads com identidade própria; operadores de recuperação possuem acesso
separado; Eurico aprova o processo e o fechamento dos incidentes, mas não opera
chaves.

## Consequências

### Positivas

- perda do host não remove a única cópia recuperável;
- uma conta humana não precisa permanecer configurada na VM;
- retenção imutável reduz exclusão acidental ou maliciosa;
- funções separadas reduzem abuso e preservam rastreabilidade;
- os cinco anos de histórico não multiplicam desnecessariamente o volume de
  backups integrais.

### Custos e riscos

- Vault, Object Storage, lifecycle, alarmes e tráfego geram custo operacional;
- uma regra de retenção bloqueada não pode ser reduzida ou removida, exigindo
  ensaio prévio;
- falha no arquivamento de WAL pode aumentar a perda e ocupar o disco do banco;
- remoção ou indisponibilidade indevida da chave pode tornar cópias inacessíveis;
- acesso SSH à VM herda os privilégios do instance principal e deve ser restrito;
- a retenção de cinco anos precisa de confirmação arquivística antes do expurgo.

## Alternativas rejeitadas

- **Guardar somente no host:** falha junto com a VM ou o volume.
- **Compartilhar chave de API entre integrantes:** elimina autoria individual e
  cria segredo de longa duração difícil de revogar.
- **Reter todo backup por cinco anos:** confunde recuperação com arquivo, amplia
  custo e exposição de dados pessoais.
- **Usar somente `pg_dump` para RPO de uma hora:** exigiria dumps frequentes e não
  oferece recuperação pontual; WAL/PITR é a estratégia adequada.
- **Bloquear a regra de retenção diretamente em produção:** uma configuração
  incorreta seria irreversível.

## Rastreabilidade

- Issue #30 — política e decisões institucionais;
- Issue #311 — backup protegido na OCI;
- Issue #312 — recuperação pontual e RPO de uma hora;
- Issue #100 — reconciliação segura após contingência;
- `docs/operations/data-retention-and-continuity.md` — plano operacional;
- `docs/security/threat-model.md` — riscos e controles.

## Referências

- [PostgreSQL 16 — Continuous Archiving and Point-in-Time Recovery](https://www.postgresql.org/docs/16/continuous-archiving.html)
- [OCI — Calling services from an instance](https://docs.oracle.com/en-us/iaas/Content/Identity/Tasks/callingservicesfrominstances.htm)
- [OCI — Securing Object Storage](https://docs.oracle.com/en-us/iaas/Content/Security/Reference/objectstorage_security.htm)
- [OCI — Object Storage Data Retention Rules](https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usingretentionrules.htm)
- [OCI — IAM Security Policies](https://docs.oracle.com/en-us/iaas/Content/Security/Reference/iam_security_topic-IAM_Security_Policies.htm)
