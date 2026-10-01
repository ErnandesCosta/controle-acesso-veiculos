# Plano de retenção, backup e continuidade

## Estado e autoridade

Este documento registra as decisões operacionais comunicadas pela equipe na
Issue #30 e validadas pelo contato principal do processo em 29 de setembro de 2026. Eurico, do Setor de Transporte, é o responsável funcional pelo processo. A
manutenção técnica pertence à equipe designada pelo curso de Engenharia de
Software, sob acompanhamento acadêmico dos orientadores do curso.

O prazo de cinco anos é uma decisão operacional do responsável pelo processo;
não é apresentado como interpretação jurídica. O setor arquivístico ou a
referência institucional de proteção de dados ainda precisa confirmar o
enquadramento aplicável antes da ativação de descarte automático.

Até a implantação e o exercício institucional:

- não existe descarte automático de registros no sistema;
- o acervo físico anterior e os novos formulários de contingência seguem o prazo
  operacional de cinco anos, sujeito à revisão arquivística formal;
- os scripts de backup permanecem restritos ao desenvolvimento e ao ensaio de CI;
- nenhum dump local deve ser tratado como backup de produção;
- o uso real depende do backup protegido na OCI e do exercício de recuperação e
  contingência previsto para a segunda unidade.

### Progresso da Issue #30

| Critério de aceite                     | Estado verificável                                                                                 |
| -------------------------------------- | -------------------------------------------------------------------------------------------------- |
| decisões e responsáveis institucionais | papéis funcional e técnico definidos; referência arquivística/proteção de dados pendente           |
| prazos com finalidade e justificativa  | prazo operacional de cinco anos aprovado; enquadramento arquivístico formal pendente               |
| backup sem credencial em texto simples | atendido pelo ensaio local; senha não entra no dump ou na linha de comando                         |
| restauração testada                    | atendido localmente e na CI com banco temporário isolado e dados fictícios                         |
| RPO e RTO iniciais                     | metas de produção definidas abaixo; comprovação técnica do RPO de uma hora pendente                |
| contingência e reconciliação           | fluxo em papel aprovado em princípio; exercício e endpoint seguro pendentes para a segunda unidade |
| guia operacional e Wiki                | guia versionado atualizado nesta issue; Wiki deve ser atualizada após o merge                      |

A Issue #30 não deve ser fechada enquanto o ambiente protegido na OCI, a revisão
arquivística e o exercício de contingência permanecerem pendentes.

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

| Grupo             | Exemplos no sistema                                                          | Finalidade do MVP                                       | Acesso esperado                                                    | Decisão de retenção                                                                   |
| ----------------- | ---------------------------------------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------------------- |
| Identidade        | nome, tipo e número de documento opcional, vínculo e e-mail                  | identificar condutor, motorista institucional e usuário | operação conforme política; gestão por Administrador               | enquanto ativa ou referenciada por registro dentro do prazo de cinco anos             |
| Credencial        | e-mail da conta, hash de senha, bloqueio e perfil                            | autenticação e autorização individual                   | serviço de autenticação e gestão administrativa                    | conta ativa; identidade mínima vinculada à auditoria pelo prazo do evento relacionado |
| Veículo           | placa, tipo, identificação de frota, marca, modelo, cor e ano                | identificar veículo e manter catálogo institucional     | operação, Transporte e Administração conforme política             | enquanto ativo ou referenciado por registro dentro do prazo de cinco anos             |
| Acesso geral      | entrada, saída, objetivo, categoria, observação e autoria                    | controlar e consultar circulação no campus              | Portaria, Vigilância, Transporte e Administração conforme política | cinco anos após o encerramento                                                        |
| Uso institucional | motorista, veículo, horários, quilometragem, itinerário e autoria            | controlar saída e retorno da frota                      | Transporte e Administração; consulta operacional limitada          | cinco anos após o encerramento                                                        |
| Evento            | responsável, período, local, pernoite, tipos, quantidades e placas opcionais | antecipar e conferir acessos autorizados                | operação, Transporte e Administração conforme política             | cinco anos após o encerramento                                                        |
| Auditoria         | ação, entidade, registro, ator, horário e transição de estado                | responsabilização, investigação e integridade           | Administrador                                                      | cinco anos após o evento auditado                                                     |
| Log técnico       | correlação, rota, status, duração e falha sem corpo ou credenciais           | diagnóstico e segurança operacional                     | equipe técnica autorizada                                          | 90 dias, salvo preservação documentada por incidente                                  |
| Backup            | cópia integral dos grupos persistidos no PostgreSQL                          | recuperação de desastre                                 | custodiante técnico autorizado                                     | janela móvel de 35 dias; não é arquivo histórico                                      |

Documento pessoal continua opcional no fluxo geral. A homologação deve confirmar
se ele é realmente necessário em cada categoria antes de ampliar a coleta.

## Prazos operacionais aprovados

Os valores abaixo registram a decisão operacional do responsável pelo processo.
Eles devem ser revistos pela referência arquivística ou de proteção de dados antes
que qualquer descarte automático seja implementado.

| Grupo                                            | Prazo operacional                                                                                                | Finalidade                                                                                      | Autoridade                                                                 |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Acessos, usos, eventos e auditorias relacionados | cinco anos após o encerramento                                                                                   | atender consulta operacional e apuração no período solicitado pelo responsável pelo processo    | responsável pelo processo; revisão arquivística/proteção de dados pendente |
| Cadastros de pessoas e veículos                  | enquanto ativos ou referenciados por registros dentro dos cinco anos                                             | preservar integridade referencial; depois eliminar ou anonimizar conforme procedimento aprovado | responsável pelo processo + proteção de dados                              |
| Contas de usuário                                | desativação imediata ao perder autorização; identidade mínima por cinco anos quando vinculada à auditoria retida | revogar acesso sem apagar autoria histórica                                                     | Administração + proteção de dados                                          |
| Logs técnicos                                    | 90 dias                                                                                                          | investigar incidentes sem duplicar o histórico de negócio de cinco anos nos logs                | manutenção técnica + segurança                                             |
| Backups de produção                              | janela móvel de 35 dias                                                                                          | recuperar falhas recentes sem transformar backup em arquivo histórico de cinco anos             | custodiante dos backups + responsável pelo processo                        |
| Formulários de contingência reconciliados        | cinco anos após o fechamento do incidente                                                                        | preservar a evidência original e a comprovação da transcrição pelo mesmo prazo operacional      | responsável pelo processo                                                  |

Uma obrigação legal, apuração, incidente ou ordem institucional pode suspender o
descarte de registros específicos. A exceção deve possuir motivo, responsável,
escopo e data de revisão. A implementação de expurgo ou anonimização só deve ser
aberta após a aprovação desta tabela e a análise dos relacionamentos do banco.

## Metas de continuidade e capacidade atual

| Indicador                 | Meta ou capacidade                                                                            | Interpretação                                                                                       |
| ------------------------- | --------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| RPO-alvo de produção      | até 1 hora                                                                                    | exige arquivamento contínuo de WAL/PITR; ainda não é atendido pelo dump atual                       |
| RPO técnico atual         | até 24 horas                                                                                  | o dump lógico diário é a capacidade intermediária e não deve ser apresentado como meta de produção  |
| RTO-alvo de produção      | até 4 horas no período com suporte disponível                                                 | prazo para restaurar um serviço utilizável; a portaria entra imediatamente em contingência no papel |
| Backup lógico             | diário, automatizado e monitorado                                                             | dump portátil no formato custom do PostgreSQL, mantido como caminho adicional de recuperação        |
| Cópia protegida           | bucket privado no OCI Object Storage, criptografado com OCI Vault e separado do host do banco | reduz perda conjunta e o acesso por um operador que possua apenas privilégios na VM do banco        |
| Teste de restauração      | trimestral e após mudança relevante na estratégia                                             | restauração integral em ambiente isolado, com evidência e tempo medido                              |
| Verificação técnica em CI | a cada alteração dos scripts ou do Compose                                                    | usa somente dados fictícios e não substitui o exercício institucional                               |

A Issue #311 prepara a cópia protegida na OCI. A Issue #312 permanece bloqueada
até esse destino e a topologia do banco existirem; ela implementará arquivamento
contínuo de WAL/PITR e comprovará o RPO de uma hora. O PostgreSQL documenta que
`pg_dump` é um backup lógico e não pode ser combinado ao replay de WAL como se
fosse um backup-base físico.

## Papéis e separação de funções

| Papel                     | Autoridade designada                                         | Responsabilidade permitida                                                                 | Proibição                                                            |
| ------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------- |
| responsável pelo processo | Eurico, Setor de Transporte                                  | aprovar fluxo operacional, exceções de retenção e fechamento de incidentes                 | manter ou operar material criptográfico                              |
| manutenção técnica        | equipe designada pelo curso de Engenharia de Software        | implantar, monitorar, criar backups e executar runbooks aprovados                          | compartilhar credenciais ou apagar permanentemente cópias protegidas |
| acompanhamento acadêmico  | orientadores do curso                                        | aprovar acessos privilegiados, designar substitutos e acompanhar exercícios                | utilizar uma única conta compartilhada                               |
| custódia de chaves        | grupo IAM restrito designado pelo curso e pelos orientadores | governar política do Vault, rotação e recuperação emergencial com revisão por duas pessoas | exportar, enviar ou versionar chaves brutas                          |
| serviço de backup         | instance principal da VM OCI                                 | enviar apenas os objetos esperados ao bucket dedicado                                      | administrar bucket, retenção, chaves ou usuários humanos             |
| operadores de recuperação | grupo técnico autorizado separadamente                       | ler cópia protegida e restaurar somente em destino isolado após aprovação                  | sobrescrever produção como parte de teste                            |
| auditoria                 | orientadores ou revisores institucionais                     | inspecionar configuração, eventos e evidências sem alterar recursos                        | operar backup ou ciclo de chaves                                     |

O acesso administrativo humano deve usar contas individuais e MFA. “Custódia
das chaves” significa autorização pelo OCI IAM e Vault, não posse de arquivo de
chave privada. O acesso será revisto em cada troca de semestre e removido quando
a pessoa deixar o projeto.

## Desenho aprovado de proteção na OCI

- bucket privado do Object Storage em compartimento dedicado à produção;
- chave de criptografia gerenciada no OCI Vault, com rotação e acesso limitados
  ao grupo de custódia;
- VM autenticada por instance principal e grupo dinâmico, sem chave de API de
  usuário armazenada no host;
- nomes de objetos únicos e temporais para dump e manifesto;
- regra temporal de retenção do Object Storage por 35 dias, testada em homologação
  antes do bloqueio, pois o bloqueio é irreversível;
- ausência de versionamento no mesmo bucket, porque a OCI não permite regra de
  retenção ativa e versionamento simultâneos;
- exclusão por lifecycle somente depois da janela protegida de 35 dias;
- monitoramento e alerta quando o backup esperado estiver ausente ou o envio falhar;
- restauração trimestral em banco isolado e após toda mudança material;
- nenhum OCID, configuração da tenancy, credencial ou dado real no repositório.

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

O procedimento local usa `pg_dump` custom e `pg_restore`, não incorpora a senha
ao arquivo, produz um manifesto SHA-256 e comprova tabelas essenciais em banco
temporário. O manifesto detecta corrupção quando preservado, mas não prova origem
se dump e manifesto forem substituídos juntos. A configuração reproduzível da
Issue #311 prepara bucket privado, chave do Vault, separação IAM, retenção,
lifecycle, alarme de ausência e publicação por instance principal. Ela ainda não
foi aplicada nem exercitada na tenancy institucional; portanto, o sistema ainda
não possui armazenamento externo ou agendamento de backup de produção comprovado.

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

O primeiro exercício no ambiente OCI está planejado para a segunda unidade
acadêmica. Até sua execução com evidências, o ambiente será classificado como
homologação e não poderá ser declarado pronto para produção institucional.

## Registro das decisões pendentes

| Decisão                                                | Responsável nominal                                                           | Data       | Resultado/evidência                                                                          |
| ------------------------------------------------------ | ----------------------------------------------------------------------------- | ---------- | -------------------------------------------------------------------------------------------- |
| dono institucional do processo e substituto            | Eurico / substituto a designar pelo Setor de Transporte                       | 29/09/2026 | responsável pelo processo confirmado; substituto pendente                                    |
| custodiante dos backups e substituto                   | equipe técnica designada pelo curso / substituto designado pelos orientadores | 29/09/2026 | papel aprovado; membros nominais do IAM pendentes do provisionamento OCI                     |
| encarregado/referência de proteção de dados            | pendente                                                                      | pendente   | pendente                                                                                     |
| tabela de retenção e exceções                          | responsável pelo processo; revisão arquivística/proteção de dados pendente    | 29/09/2026 | cinco anos para registros operacionais e papel; 90 dias para logs; 35 dias para backups      |
| RPO e RTO                                              | responsável pelo processo + manutenção técnica                                | 29/09/2026 | metas de RPO de 1 hora e RTO de 4 horas; prova pendente nas Issues #311 e #312               |
| destino protegido, chaves e acesso ao backup           | orientadores do curso + manutenção técnica                                    | 29/09/2026 | OCI Object Storage + Vault + separação IAM aprovados; provisionamento pendente na #311       |
| canal de acionamento e escalonamento                   | pendente                                                                      | pendente   | pendente                                                                                     |
| formulário mínimo de contingência                      | responsável pelo processo                                                     | 29/09/2026 | versão atual aceita como base para o exercício da segunda unidade                            |
| responsáveis pela digitação, conferência e fechamento  | operador da portaria / segundo conferente / Eurico ou delegado                | 29/09/2026 | nomes variam por turno; autoria individual é obrigatória                                     |
| periodicidade do exercício de restauração/contingência | orientadores do curso + responsável pelo processo                             | 29/09/2026 | trimestral após produção e depois de mudança material; primeiro exercício na segunda unidade |

## Referências

- [Lei nº 13.709/2018 — LGPD](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm)
- [Guia de Segurança da Informação da ANPD](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-vf.pdf/@@display-file/file)
- [PostgreSQL 16 — Backup and Restore](https://www.postgresql.org/docs/16/backup.html)
- [PostgreSQL 16 — Continuous Archiving and PITR](https://www.postgresql.org/docs/16/continuous-archiving.html)
- [OCI — Calling services from an instance](https://docs.oracle.com/en-us/iaas/Content/Identity/Tasks/callingservicesfrominstances.htm)
- [OCI — Securing Object Storage](https://docs.oracle.com/en-us/iaas/Content/Security/Reference/objectstorage_security.htm)
- [OCI — Object Storage retention rules](https://docs.oracle.com/en-us/iaas/Content/Object/Tasks/usingretentionrules.htm)
- [NIST SP 800-34 Rev. 1 — Contingency Planning Guide](https://csrc.nist.gov/pubs/sp/800/34/r1/final)
