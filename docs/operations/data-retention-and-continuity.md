# Plano de retenção, backup e continuidade

## Estado e autoridade

Este documento é a proposta técnica inicial da Issue #30. Ele organiza decisões
para homologação, mas **não representa uma política institucional aprovada**. Os
prazos, responsáveis e metas somente se tornam obrigatórios depois da validação
do IFPE — Campus Belo Jardim, incluindo o responsável pelo processo, a área de TI
e o encarregado ou referência institucional de proteção de dados.

Até essa aprovação:

- não existe descarte automático de registros no sistema;
- o acervo físico anterior segue a regra institucional vigente;
- os scripts de backup permanecem restritos ao desenvolvimento e ao ensaio de CI;
- nenhum dump local deve ser tratado como backup de produção.

### Progresso da Issue #30

| Critério de aceite | Estado verificável |
|---|---|
| decisões e responsáveis institucionais | pendente de reunião e nomes formais |
| prazos com finalidade e justificativa | proposta registrada abaixo; pendente de aprovação |
| backup sem credencial em texto simples | atendido pelo ensaio local; senha não entra no dump ou na linha de comando |
| restauração testada | atendido localmente e na CI com banco temporário isolado e dados fictícios |
| RPO e RTO iniciais | propostos abaixo; pendentes de aprovação |
| contingência e reconciliação | fluxo proposto; exercício e endpoint seguro pendentes |
| guia operacional e Wiki | guia versionado nesta issue; Wiki deve ser atualizada após o merge |

A Issue #30 não deve ser fechada enquanto os itens institucionais e o exercício de
contingência permanecerem pendentes.

## Princípios

1. O registro digital deve ser a fonte principal depois da homologação.
2. Papel é contingência temporária, não uma segunda base permanente.
3. Somente dados necessários à finalidade operacional devem ser coletados.
4. Backup é uma cópia para recuperação; não substitui arquivo histórico nem
   autoriza retenção indefinida.
5. Exclusão, anonimização, retenção excepcional e bloqueio por investigação
   precisam ser autorizados, rastreáveis e aplicados também ao ciclo dos backups.
6. Um backup só é recuperável quando uma restauração isolada foi comprovada.

Esses princípios seguem a finalidade e a necessidade previstas no artigo 6º da
LGPD e o término e a conservação do tratamento previstos nos artigos 15 e 16.
A definição da hipótese legal e da tabela de temporalidade é responsabilidade
institucional, não da equipe de desenvolvimento.

## Inventário e finalidade

| Grupo | Exemplos no sistema | Finalidade do MVP | Acesso esperado | Decisão de retenção |
|---|---|---|---|---|
| Identidade | nome, tipo e número de documento opcional, vínculo e e-mail | identificar condutor, motorista institucional e usuário | operação conforme política; gestão por Administrador | pendente |
| Credencial | e-mail da conta, hash de senha, bloqueio e perfil | autenticação e autorização individual | serviço de autenticação e gestão administrativa | pendente |
| Veículo | placa, tipo, identificação de frota, marca, modelo, cor e ano | identificar veículo e manter catálogo institucional | operação, Transporte e Administração conforme política | pendente |
| Acesso geral | entrada, saída, objetivo, categoria, observação e autoria | controlar e consultar circulação no campus | Portaria, Vigilância, Transporte e Administração conforme política | pendente |
| Uso institucional | motorista, veículo, horários, quilometragem, itinerário e autoria | controlar saída e retorno da frota | Transporte e Administração; consulta operacional limitada | pendente |
| Evento | responsável, período, local, pernoite, tipos, quantidades e placas opcionais | antecipar e conferir acessos autorizados | operação, Transporte e Administração conforme política | pendente |
| Auditoria | ação, entidade, registro, ator, horário e transição de estado | responsabilização, investigação e integridade | Administrador | pendente |
| Log técnico | correlação, rota, status, duração e falha sem corpo ou credenciais | diagnóstico e segurança operacional | equipe técnica autorizada | pendente |
| Backup | cópia integral dos grupos persistidos no PostgreSQL | recuperação de desastre | custodiante técnico autorizado | pendente |

Documento pessoal continua opcional no fluxo geral. A homologação deve confirmar
se ele é realmente necessário em cada categoria antes de ampliar a coleta.

## Proposta de prazos para decisão

Os valores abaixo são uma **hipótese operacional**, não uma interpretação jurídica.
Eles tornam a conversa objetiva e devem ser aprovados, substituídos ou rejeitados.

| Grupo | Proposta inicial | Justificativa a validar | Decisor |
|---|---|---|---|
| Acessos, usos, eventos e auditorias relacionados | 24 meses após o encerramento | permitir consulta entre períodos letivos e apuração posterior sem manter histórico indefinido | responsável pelo processo + proteção de dados |
| Cadastros de pessoas e veículos | enquanto ativos ou referenciados por registros ainda retidos | preservar integridade referencial; depois avaliar eliminação ou anonimização | responsável pelo processo + proteção de dados |
| Contas de usuário | desativação imediata ao perder autorização; dados mínimos enquanto houver auditoria vinculada | revogar acesso sem apagar autoria histórica | Administração + proteção de dados |
| Logs técnicos | 30 dias | diagnóstico e investigação com exposição reduzida | TI + segurança |
| Backups de produção | janela móvel de 30 dias | recuperar falhas recentes sem transformar backup em arquivo permanente | TI + responsável pelo processo |
| Formulários de contingência reconciliados | até a conferência e pelo prazo adicional aprovado; depois descarte seguro | comprovar a reconciliação sem manter duplicata permanente | responsável pelo processo |

Uma obrigação legal, apuração, incidente ou ordem institucional pode suspender o
descarte de registros específicos. A exceção deve possuir motivo, responsável,
escopo e data de revisão. A implementação de expurgo ou anonimização só deve ser
aberta após a aprovação desta tabela e a análise dos relacionamentos do banco.

## Metas iniciais de continuidade

| Indicador | Proposta para homologação | Interpretação |
|---|---|---|
| RPO | até 24 horas | no pior caso aceito, perde-se no máximo o intervalo desde o último backup diário |
| RTO | até 4 horas no período com suporte disponível | prazo para restaurar um serviço utilizável; a portaria entra em contingência imediatamente |
| Backup | diário, automatizado e monitorado | dump consistente em formato custom do PostgreSQL |
| Cópia protegida | fora do host do banco, criptografada e não sincronizada em tempo real | reduz perda conjunta e propagação imediata de corrupção ou ransomware |
| Teste de restauração | trimestral e após mudança relevante na estratégia | restauração integral em ambiente isolado, com evidência e tempo medido |
| Verificação técnica em CI | a cada alteração dos scripts ou do Compose | usa somente dados fictícios e não substitui o exercício institucional |

Se o IFPE exigir RPO menor que 24 horas, um dump diário deixa de ser suficiente.
Nesse caso deve ser planejado arquivamento contínuo de WAL/PITR ou serviço gerenciado
equivalente, com custo, operação e testes próprios.

## Controles mínimos do backup de produção

- conta de serviço exclusiva e com menor privilégio possível;
- segredo fornecido por cofre ou mecanismo equivalente, nunca no dump, nome do
  arquivo, log, repositório ou linha de comando exposta;
- criptografia em trânsito e em repouso, com acesso e rotação de chaves definidos;
- armazenamento separado do host e do volume primário do PostgreSQL;
- registro de início, término, tamanho, integridade, destino e resultado, sem dados
  pessoais ou credenciais no log;
- alerta para ausência ou falha do backup esperado;
- expiração automática conforme a janela aprovada e descarte seguro;
- restauração sempre em destino isolado por padrão; substituir produção exige
  autorização explícita e registro do incidente;
- inspeção da origem antes da restauração, pois um dump deve ser tratado como
  conteúdo confiável somente quando sua procedência e integridade são conhecidas.

O procedimento local atual usa `pg_dump` custom e `pg_restore`, não incorpora a
senha ao arquivo, produz um manifesto SHA-256 e comprova tabelas essenciais em
banco temporário. O manifesto detecta corrupção quando preservado, mas não prova
origem se dump e manifesto forem substituídos juntos. O ensaio é uma base de
teste, mas ainda não oferece armazenamento externo, criptografia, assinatura ou
agendamento de produção.

## Contingência da portaria

### Ativação

O Porteiro ou Vigilante ativa a contingência quando a aplicação não permite
consultar ou registrar após a verificação básica de energia, rede e dispositivo.
Deve ser anotado um identificador único do incidente, início, motivo percebido,
responsável pelo acionamento e pessoas que assumiram o registro manual. A TI e o
Setor de Transporte devem ser avisados pelo canal definido na homologação.

### Registro mínimo em papel

Cada linha recebe número sequencial dentro do incidente e registra apenas:

- entrada ou saída e data/hora observada;
- placa e tipo do veículo;
- nome do condutor;
- objetivo e categoria;
- referência de evento, quando existir;
- observação indispensável;
- nome ou matrícula operacional de quem registrou.

Um [modelo de formulário](contingency-record-template.md) acompanha este plano
para a simulação e deve ser ajustado conforme o retorno da portaria.

Documento pessoal não deve ser copiado por padrão. Se a instituição concluir que
uma categoria exige documento, a finalidade e o prazo precisam constar na política.

### Operação durante a indisponibilidade

- manter juntos os registros de entrada e saída pelo número sequencial;
- destacar veículos que permaneceram no campus quando o sistema voltou;
- não compartilhar foto da folha por aplicativo pessoal;
- guardar o formulário em local de acesso restrito até a reconciliação;
- não tentar reconstruir o banco diretamente nem usar credenciais compartilhadas.

## Recuperação e reconciliação

1. A TI confirma saúde da API e do banco e registra o horário de recuperação.
2. O responsável operacional encerra a contingência e conta as linhas produzidas.
3. Uma pessoa digita e outra confere placa, horários, categoria, evento e situação
   de cada veículo; divergências ficam registradas, não são corrigidas no papel sem
   ressalva.
4. O sistema associa cada item ao identificador e número sequencial da contingência,
   preserva horários observados, autor da digitação, autor do papel e momento da
   reconciliação.
5. Registros ainda abertos são conferidos fisicamente antes de qualquer encerramento.
6. O Setor de Transporte confere total de linhas, reconciliadas, rejeitadas e
   pendentes e aprova o fechamento do incidente.
7. O formulário recebe a evidência de conferência e segue o prazo de descarte
   aprovado; ele não vira arquivo paralelo permanente.

O backend atual **não possui** um endpoint seguro para o passo 4: os endpoints
operacionais usam horário e ator atuais do servidor. Até existir e ser homologado
um fluxo específico, não se deve lançar um registro histórico como se tivesse
ocorrido no momento da digitação. Essa lacuna deve permanecer visível e rastreada
em issue própria.

## Exercício de homologação

O aceite institucional deve incluir um cenário controlado:

1. declarar indisponibilidade e iniciar formulário numerado;
2. simular ao menos uma entrada, uma saída e um veículo ainda presente;
3. recuperar o ambiente e medir o tempo até `/health/ready` saudável;
4. reconciliar com dupla conferência sem alterar autoria ou horário observado;
5. comparar quantidades e registrar divergências;
6. decidir destino e descarte do papel;
7. registrar RPO, RTO, responsáveis, canais e prazos aprovados.

## Registro das decisões pendentes

| Decisão | Responsável nominal | Data | Resultado/evidência |
|---|---|---|---|
| dono institucional do processo e substituto | pendente | pendente | pendente |
| custodiante dos backups e substituto | pendente | pendente | pendente |
| encarregado/referência de proteção de dados | pendente | pendente | pendente |
| tabela de retenção e exceções | pendente | pendente | pendente |
| RPO e RTO | pendente | pendente | pendente |
| destino protegido, chaves e acesso ao backup | pendente | pendente | pendente |
| canal de acionamento e escalonamento | pendente | pendente | pendente |
| formulário mínimo de contingência | pendente | pendente | pendente |
| responsáveis pela digitação, conferência e fechamento | pendente | pendente | pendente |
| periodicidade do exercício de restauração/contingência | pendente | pendente | pendente |

## Referências

- [Lei nº 13.709/2018 — LGPD](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm)
- [Guia de Segurança da Informação da ANPD](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-vf.pdf/@@display-file/file)
- [PostgreSQL 16 — Backup and Restore](https://www.postgresql.org/docs/16/backup.html)
- [NIST SP 800-34 Rev. 1 — Contingency Planning Guide](https://csrc.nist.gov/pubs/sp/800/34/r1/final)
