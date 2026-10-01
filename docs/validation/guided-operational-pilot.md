# Piloto operacional acompanhado do MVP

**Status:** planejado; ainda não iniciado<br>
**Escopo:** treinamento, observação de uso, descoberta de defeitos e coleta de
feedback no ambiente real da portaria<br>
**Rastreabilidade:** Issues #30, #100, #162, #311, #382 e #388

Este runbook orienta um período acompanhado com usuários reais. Ele não autoriza
dados pessoais reais, substituição das planilhas, abertura para produção ou
implantação na OCI. Cada avanço depende dos gates descritos abaixo e deve deixar
evidência sanitizada.

## 1. Conceitos que não podem ser confundidos

| Conceito      | Significado neste projeto                                                              |
| ------------- | -------------------------------------------------------------------------------------- |
| Usuário real  | Pessoa que exerce ou representa um dos perfis e avalia o sistema                       |
| Ambiente real | Dispositivo, rede, luminosidade, interrupções e rotina da portaria                     |
| Dado real     | Registro relativo a pessoa ou veículo que efetivamente circulou no campus              |
| Piloto        | Uso limitado, acompanhado, reversível e com critérios explícitos de pausa              |
| Produção      | Processo institucional oficial, com operação, suporte e responsabilidades formalizados |

Usuários reais podem treinar no ambiente real com dados fictícios. Isso não
transforma o piloto em produção nem autoriza o tratamento de dados reais.

## 2. Governança

| Papel                        | Responsabilidade durante o piloto                                                     |
| ---------------------------- | ------------------------------------------------------------------------------------- |
| Eurico / Setor de Transporte | validar o fluxo, indicar participantes e decidir prioridades funcionais               |
| Porteiro e Vigilante         | executar tarefas, relatar dificuldades e confirmar a rotina observada                 |
| Administradores funcionais   | provisionar contas, apoiar recuperação de acesso e supervisionar permissões           |
| Equipe técnica do curso      | implantar, acompanhar saúde, proteger evidências, corrigir defeitos e operar rollback |
| Orientadores                 | acompanhar riscos, uso da tenancy, segurança e passagem de responsabilidade           |

Nenhum integrante isolado pode declarar produção, ampliar a coleta de dados ou
remover a contingência. Incidentes de segurança seguem o canal privado definido
em [`SECURITY.md`](../../SECURITY.md), e não uma issue pública.

## 3. Fases e gates

### Fase 0 — preparação

Antes de convidar usuários:

- registrar versão, commit, ambiente, dispositivos e responsáveis;
- confirmar HTTPS, contas individuais e menor privilégio;
- comprovar health checks, monitoramento, rollback e canal de suporte;
- revisar custo, orçamento e desligamento conforme a Issue #382;
- usar banco exclusivo do piloto;
- testar o roteiro com dados fictícios e os quatro perfis;
- confirmar como acionar a contingência em papel;
- comunicar finalidade, duração, dados coletados e forma de registrar feedback.

**Gate de saída:** todos os itens anteriores foram conferidos e o ambiente não
contém credenciais padrão, dados pessoais importados ou dependência de um único
integrante sem substituto.

### Fase 1 — treinamento guiado

- usar somente contas e dados fictícios;
- executar entrada, consulta, saída, histórico e tarefas específicas do perfil;
- testar no equipamento real, durante o dia e, quando aplicável, à noite;
- observar teclado, toque, legibilidade, contraste, interrupções e retomada;
- orientar o usuário sem alterar código ou regra durante a sessão;
- registrar dúvidas sem nomear o participante.

**Gate de saída:** tarefas críticas foram concluídas sem defeito bloqueante, os
participantes sabem acionar ajuda e a equipe consegue restaurar o ambiente
fictício sem procedimento improvisado.

### Fase 2 — operação assistida em paralelo

- manter o processo institucional vigente como fonte oficial;
- usar no sistema apenas cenários fictícios ou sanitizados;
- acompanhar o turno sem induzir respostas;
- observar sequência de pico, troca de operador, falha de rede e retomada;
- comparar esforço e informação necessária, sem copiar registros pessoais do
  papel para o ambiente de teste.

**Gate de saída:** o fluxo foi observado com Porteiro e Vigilante, os problemas
foram classificados e não existe defeito aberto que possa autorizar acesso
indevido, perder registro ou impedir contingência.

### Fase 3 — piloto operacional controlado

Esta fase só pode começar após decisão institucional registrada fora do código,
com escopo, período, responsáveis, categorias de dados e processo oficial
definidos. Antes do primeiro registro real também são obrigatórios:

- implantação e suporte operacional da Issue #382;
- backup protegido e restauração isolada comprovados pela Issue #311;
- contingência e reconciliação validadas na Issue #30;
- contas individuais, auditoria, retenção e descarte aplicáveis;
- aviso aos participantes e canal para incidente de privacidade;
- decisão explícita sobre convivência ou substituição das planilhas.

Ausência de qualquer item mantém o piloto nas fases 1 ou 2. O retorno positivo
em uma demonstração não substitui esse gate.

### Fase 4 — encerramento e decisão

- encerrar ou revogar acessos temporários;
- preservar somente as evidências autorizadas;
- consolidar métricas agregadas e decisões;
- vincular cada correção ou melhoria a uma issue separada;
- registrar `avançar`, `repetir`, `restringir` ou `interromper`, com responsável;
- confirmar desligamento de recursos que não continuarão em uso;
- atualizar documentação e Wiki somente com o estado comprovado.

## 4. Checklist diário

### Antes do turno

- [ ] versão e ambiente esperados;
- [ ] API, frontend e banco saudáveis;
- [ ] HTTPS e horário do dispositivo corretos;
- [ ] conta individual disponível para cada operador;
- [ ] nenhum segredo, token ou painel técnico projetado;
- [ ] contingência acessível;
- [ ] responsável técnico e contato funcional disponíveis;
- [ ] custo e alertas da OCI sem anomalia.

### Durante o turno

- [ ] registrar somente a informação autorizada para a fase;
- [ ] não compartilhar conta nem manter sessão do operador anterior;
- [ ] anotar tarefa, perfil, dispositivo, resultado e necessidade de ajuda;
- [ ] retirar dados pessoais de capturas e relatos;
- [ ] abrir incidente privado para suspeita de segurança ou privacidade;
- [ ] pausar o piloto quando um critério da seção 7 ocorrer.

### Depois do turno

- [ ] conferir acessos abertos e divergências sem corrigir evidência silenciosamente;
- [ ] registrar bugs e sugestões com reprodução sanitizada;
- [ ] conferir saúde, alertas e execução esperada do backup, quando aplicável;
- [ ] revogar acesso que deixou de ser necessário;
- [ ] registrar decisão para o turno seguinte.

## 5. Observação e métricas mínimas

As métricas avaliam o produto, não o desempenho individual. Não registrar nome,
matrícula, senha, documento, placa real ou imagem do participante.

| Campo             | Valores sugeridos                                           |
| ----------------- | ----------------------------------------------------------- |
| Perfil            | Porteiro, Vigilante, Transporte ou Administrador            |
| Contexto          | treinamento, rotina, pico, troca de turno ou contingência   |
| Dispositivo       | computador ou tablet, com navegador e resolução             |
| Tema e iluminação | claro/escuro/sistema; dia/noite                             |
| Resultado         | sem ajuda, com ajuda ou não concluído                       |
| Esforço           | tempo aproximado ou faixa agregada, sem ranking individual  |
| Ocorrência        | dúvida, erro de uso, defeito, indisponibilidade ou sugestão |
| Decisão           | aceito, ajustar, rejeitado ou pendente                      |

Consolidar por tarefa e perfil. Uma observação isolada pode indicar investigação,
mas não deve ser apresentada como preferência de todos os usuários.

## 6. Triagem de ocorrências

| Severidade      | Exemplo                                                                       | Resposta                                                          |
| --------------- | ----------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| S0 — crítica    | exposição de segredo ou dado, acesso indevido, corrupção ou perda de registro | pausar imediatamente, preservar evidência e usar o canal privado  |
| S1 — bloqueante | fluxo essencial indisponível e sem alternativa segura no sistema              | acionar contingência, impedir avanço da fase e priorizar correção |
| S2 — relevante  | tarefa concluída apenas com contorno ou risco de erro operacional             | registrar issue e avaliar antes do próximo ciclo                  |
| S3 — moderada   | inconsistência visual, texto confuso ou dificuldade não bloqueante            | incluir no backlog com evidência e contexto                       |
| S4 — sugestão   | preferência ou melhoria sem defeito comprovado                                | validar recorrência e benefício antes de implementar              |

A issue pública deve conter passos reproduzíveis, comportamento esperado e
observado, versão, perfil e dispositivo, sempre com valores fictícios. Nunca
anexar banco, log integral, cookie, token, credencial ou captura com dado real.

## 7. Critérios de pausa imediata

Interromper o piloto e usar a contingência quando ocorrer:

- suspeita de acesso indevido, vazamento ou comprometimento de credencial;
- decisão de autorização diferente da política conhecida;
- perda, corrupção ou duplicação não explicada de registro;
- impossibilidade de identificar o operador responsável;
- indisponibilidade sem procedimento seguro de continuidade;
- falha de backup quando a fase depender de persistência operacional;
- exposição pública de banco, administração ou serviço sem HTTPS;
- custo inesperado ou perda de controle da tenancy;
- ausência do responsável necessário para decidir ou responder ao incidente.

Retomar somente depois de registrar causa, impacto, correção, validação e decisão
do responsável adequado. Não apagar evidência para “limpar” o ambiente.

## 8. Registro sanitizado da sessão

```text
Data e janela:
Versão / commit:
Ambiente e dispositivo:
Perfis representados:
Fase executada:
Tarefas observadas:
Resultados agregados:
Ocorrências por severidade:
Issues relacionadas:
Contingência acionada: sim / não
Decisão: avançar / repetir / restringir / interromper
Responsáveis pela decisão:
Pendências e próximo acompanhamento:
```

Esse registro não deve conter nomes dos participantes nem dados das movimentações.
Quando uma evidência operacional real for necessária, ela deve permanecer no
repositório institucional autorizado, com acesso e retenção próprios — nunca no
GitHub público do projeto.
