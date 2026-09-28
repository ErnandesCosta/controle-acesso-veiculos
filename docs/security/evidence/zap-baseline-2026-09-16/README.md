# Amostra do baseline passivo do OWASP ZAP

Esta pasta preserva uma amostra histórica dos relatórios produzidos pelo job
`Run integrated Compose smoke test` da execução bem-sucedida
[`35054736853`](https://github.com/ifpebj-ti/controle-acesso-veiculos/actions/runs/35054736853),
em 16 de setembro de 2026, sobre o commit
`e7d6f37739f89fd3d8733dc66420bb3d0439b44f` da `main`.

## Escopo da execução

- ferramenta: OWASP ZAP 2.17.0 fixado por versão e digest;
- modalidade: Baseline Scan com spider tradicional e análise passiva;
- alvo: `http://frontend:8080` na rede interna de uma stack Compose descartável;
- autenticação: nenhuma;
- superfície observada: frontend público e proxy reverso público;
- resultado: zero alertas altos, zero médios, um baixo e três informativos;
- conclusão do workflow: aprovado conforme a política versionada em
  `infrastructure/security/zap-baseline.conf`.

O alerta baixo sobre `Cross-Origin-Embedder-Policy` e os achados informativos
permanecem visíveis nos relatórios. A aprovação do workflow significa que nenhum
achado classificado como bloqueante foi encontrado; não significa ausência de
vulnerabilidades.

## Arquivos preservados

| Arquivo                              | Finalidade                       | SHA-256                                                            |
| ------------------------------------ | -------------------------------- | ------------------------------------------------------------------ |
| [`zap-report.html`](zap-report.html) | leitura visual no navegador      | `98fc2f0b928c67096e49eb40283c888193f98c4892cd209f6277488c3f3a49a5` |
| [`zap-report.json`](zap-report.json) | inspeção estruturada e automação | `1bad763eaa04f98905b7405627d1f42826fc25b364867dbf68de213ec049a25c` |
| [`zap-report.md`](zap-report.md)     | leitura e revisão no GitHub      | `fba5826b6570d4c39bd43a8bcc6df94416554551449b27b2e6aacf27361c9733` |

Os três arquivos foram preservados sem alteração em relação ao artefato baixado
da CI. Antes do versionamento, foram verificados contra credenciais, tokens,
cookies, segredos, dados pessoais e caminhos locais; o conteúdo referencia
somente o host descartável `frontend:8080` e recursos públicos analisados.

## Limitações

Esta amostra:

- não representa automaticamente o estado atual da `main`;
- não cobre login, autorização ou rotas autenticadas;
- não executa ataques ativos;
- não substitui os relatórios de cada execução da CI, revisão humana ou pentest;
- não autoriza varredura de produção ou de infraestrutura institucional.

Downloads futuros continuam em `artifacts/`, que permanece ignorado pelo Git. Só
uma evidência revisada e vinculada a uma Issue deve ser promovida para esta pasta.
