# Segurança do projeto

Esta pasta contém a fonte versionada dos documentos de segurança do projeto.

- [Autenticação e autorização](authentication.md)
- [Modelagem de ameaças](threat-model.md)
- [Modelo visual para OWASP Threat Dragon](controle-acesso-veiculos-threat-model.json)
- [Guia de desenvolvimento seguro](secure-development-guide.md)
- [Análise dinâmica com OWASP ZAP](dynamic-application-security-testing.md)
- [Amostra histórica do baseline passivo do ZAP](evidence/zap-baseline-2026-09-16/README.md)
- [ADR 0001 — ciclo de vida seguro de sessões](../architecture/decisions/0001-secure-session-lifecycle.md)
- [ADR 0002 — retenção de dados e backup protegido na OCI](../architecture/decisions/0002-data-retention-and-oci-backup.md)

A Wiki apresenta cópias de leitura e contexto acadêmico, mas mudanças nestes
documentos devem passar por Issue, branch, revisão e Pull Request.

Documentos relacionados:

- `.github/instructions/security.instructions.md` — regras obrigatórias para
  alterações assistidas no repositório;
- página `Segurança e Privacidade` da Wiki — decisões, dados e pendências do
  Projeto Integrador;
- Issue #26 — criação e validação inicial destes documentos.

## Abrir e apresentar o modelo visual

O arquivo `controle-acesso-veiculos-threat-model.json` usa o formato nativo 2.x
do OWASP Threat Dragon. Para revisá-lo sem enviar dados do projeto a um serviço
externo:

1. instale a versão desktop 2.x publicada pelo projeto OWASP;
2. abra uma sessão local;
3. importe o arquivo JSON desta pasta;
4. abra o diagrama **Fluxo principal e cadeia de entrega**;
5. selecione cada elemento para consultar ameaças, estado e mitigação;
6. use **Report** para gerar o PDF que será apresentado ou anexado à evidência
   acadêmica.

Antes da apresentação, confira se o relatório contém:

- 1 ator, 5 processos, 4 depósitos, 8 fluxos e 5 fronteiras;
- as ameaças `TM-01` a `TM-23` sem duplicidade;
- distinção entre controles mitigados e riscos ainda abertos;
- desenho de backup na OCI como decisão aprovada, mas provisionamento, HTTPS,
  observabilidade operada e homologação como pendências, não funcionalidades prontas.

Não inclua credenciais, endereços internos, dados pessoais, capturas de produção
ou segredos no modelo ou no PDF exportado.
