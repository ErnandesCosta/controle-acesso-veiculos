# Política de Segurança

## Versões suportadas

Este é um projeto acadêmico com releases técnicas, ainda não liberado para
produção institucional. A equipe avalia correções de segurança para a versão
mais recente e para a branch `main`, sem oferecer suporte comercial ou SLA.

| Referência                   | Estado de suporte                                                |
| ---------------------------- | ---------------------------------------------------------------- |
| release técnica mais recente | avaliada para correções de segurança                             |
| `main`                       | desenvolvimento ativo; pode conter mudanças ainda não publicadas |
| releases anteriores          | sem garantia de correção retroativa                              |

Uma release técnica não representa homologação, implantação ou autorização de
uso com dados reais.

## Como relatar uma vulnerabilidade

Não abra uma issue pública com detalhes exploráveis. Use o
[reporte privado de vulnerabilidade do GitHub](https://github.com/ifpebj-ti/controle-acesso-veiculos/security/advisories/new).

Inclua somente o necessário para a triagem:

- componente e versão ou commit afetado;
- pré-condições e passos mínimos para reprodução;
- impacto observado ou potencial;
- evidências sanitizadas, sem credenciais, tokens, dados pessoais ou endereços
  internos;
- mitigação sugerida, quando conhecida.

Não envie senha, chave, cookie, dump de banco, documento real, planilha da
portaria ou dado de pessoa e veículo. Se uma evidência contiver informação
sensível, descreva sua existência e aguarde orientação antes de compartilhá-la.

## Processo de triagem

A equipe acadêmica buscará:

1. confirmar o recebimento quando houver disponibilidade;
2. reproduzir o problema em ambiente local ou descartável;
3. classificar impacto, alcance e versões afetadas;
4. preparar correção, testes e atualização da modelagem de ameaças quando
   aplicável;
5. coordenar a divulgação depois que uma mitigação segura estiver disponível.

Os prazos dependem da gravidade, do calendário acadêmico e do acesso aos
responsáveis institucionais. O relator deve evitar divulgação pública enquanto a
análise e a correção estiverem em andamento.

## Limites para pesquisa e testes

- Não teste contra redes, equipamentos ou ambientes institucionais sem
  autorização formal.
- Não execute negação de serviço, engenharia social, persistência, exfiltração
  ou alteração de dados.
- Prefira dados fictícios e o ambiente local documentado no
  [README](README.md).
- Achados em dependências devem indicar o pacote, a versão e a referência pública
  da vulnerabilidade, sem incluir exploits desnecessários.

Para conhecer os controles atuais e os riscos residuais, consulte a
[modelagem de ameaças](docs/security/threat-model.md) e o
[guia de desenvolvimento seguro](docs/security/secure-development-guide.md).
