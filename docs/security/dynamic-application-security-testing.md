# Análise dinâmica de segurança

## Estado e finalidade

A Issue #227 introduz uma baseline de DAST com OWASP ZAP 2.17.0. O scanner
observa o comportamento HTTP do sistema em execução e complementa, sem
substituir, análise de código, testes, Dependency Review, Trivy, SBOM e revisão
humana.

O recorte inicial cobre somente a superfície pública e não autenticada do
frontend e de seu proxy para a API. Ele não comprova ausência de vulnerabilidades
nem representa um teste de invasão completo.

## Isolamento da execução

O scan executa dentro do job de smoke test integrado:

1. o workflow gera credenciais efêmeras e portas aleatórias;
2. PostgreSQL, API e frontend são construídos em um projeto Compose exclusivo;
3. readiness e verificações HTTP precisam ser aprovadas;
4. o container do ZAP entra apenas na rede descartável do Compose e acessa
   `http://frontend:8080`;
5. relatórios HTML, JSON e Markdown são preservados por 14 dias;
6. containers, rede e volume são removidos mesmo quando o scan falha.

A imagem oficial
`ghcr.io/zaproxy/zaproxy:2.17.0@sha256:781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef`
está fixada por versão e digest. O container perde todas as capabilities Linux e
usa `no-new-privileges`.

## Política de alertas

A configuração versionada está em
`infrastructure/security/zap-baseline.conf`:

- `FAIL`: falha de segurança que exige correção ou aceitação de risco revisada e
  reprova o job;
- `WARN`: hipótese, achado informativo ou controle não exigido pela arquitetura
  atual; permanece visível no log e no relatório;
- regras novas ou ainda não classificadas permanecem `WARN` por padrão e devem
  ser revisadas, nunca ignoradas em massa;
- erro do scanner ou relatório ausente reprova o job.

A ausência de `Cross-Origin-Embedder-Policy` permanece como aviso: a aplicação
atual usa recursos same-origin, CSP restritiva e
`Cross-Origin-Resource-Policy: same-origin`, sem necessidade de isolamento para
`SharedArrayBuffer`. Adicionar o cabeçalho apenas para silenciar a ferramenta
poderia alterar compatibilidade sem reduzir um risco aplicável.

Os avisos informativos de aplicação web moderna, cache de arquivos públicos e
comentário suspeito no bundle minificado também continuam visíveis. Eles não são
suprimidos e devem ser reavaliados quando o frontend ou a política de cache
mudar.

## Baseline inicial

Em 15 de setembro de 2026, a execução local reproduzível percorreu sete URLs:

- 63 regras passivas sem achados;
- nenhum alerta classificado como `FAIL`;
- quatro categorias em `WARN`, incluindo o cabeçalho COEP e três achados
  informativos;
- relatórios produzidos sem contas, senhas, dados pessoais ou chamadas a
  ambiente externo.

O resultado oficial para integração é a execução do GitHub Actions no commit do
Pull Request. Resultados locais são evidência adicional, não substituta.

Uma [amostra histórica sanitizada](evidence/zap-baseline-2026-09-16/README.md)
da execução bem-sucedida `35054736853` preserva os relatórios HTML, JSON e
Markdown para consulta acadêmica mesmo após a expiração do artefato da CI. Ela
documenta o commit e o escopo examinados e não deve ser tratada como resultado
atual da `main`.

## Limites e evolução

O Baseline Scan usa spider tradicional e análise passiva; ele não envia ataques
ativos. Fluxos protegidos por login e autorização não são alcançados nesta fase.

Scan autenticado por perfil e Active Scan exigem outra Issue, contas totalmente
fictícias, ambiente descartável, autorização explícita, limites de escopo e
revisão do risco de alteração de dados. Active Scan nunca deve apontar para
produção ou para ambiente institucional sem autorização formal.

## Referências

- [ZAP Baseline Scan](https://www.zaproxy.org/docs/docker/baseline-scan/)
- [ZAP Docker User Guide](https://www.zaproxy.org/docs/docker/about/)
- [ZAP basic penetration test](https://www.zaproxy.org/docs/desktop/start/pentest/)
