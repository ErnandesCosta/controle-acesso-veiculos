# Guia de desenvolvimento seguro

## Objetivo

Definir práticas mínimas para mudanças no Controle de Acesso de Veículos. O guia
se aplica a frontend, backend, banco, testes, containers, CI/CD, documentação e
operação. Ele complementa as instruções obrigatórias em `.github/instructions`.

**Rastreabilidade:** Issue #26

## Referências

- [OWASP ASVS](https://owasp.org/www-project-application-security-verification-standard/)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)
- [NIST Secure Software Development Framework](https://csrc.nist.gov/pubs/sp/800/218/final)
- [Microsoft Threat Modeling Tool](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool)
- [Docker build security](https://docs.docker.com/build/building/secrets/)

Essas referências orientam controles técnicos; requisitos institucionais do IFPE
devem ser validados pelos responsáveis competentes.

## Fluxo obrigatório de mudança

1. Vincular a mudança a uma Issue com escopo e risco.
2. Trabalhar em branch própria.
3. Identificar dados, atores e fronteiras afetadas.
4. Implementar o menor escopo seguro.
5. Adicionar testes positivos, negativos e de autorização quando aplicável.
6. Auditar diff, dependências, migrations, logs e documentação.
7. Exigir CI e revisão antes do merge.
8. Atualizar a modelagem de ameaças quando o desenho mudar.

## Segredos e configuração

- nunca versionar `.env`, senha, token, chave ou connection string real;
- usar valores obviamente fictícios em exemplos;
- fornecer segredos por configuração do ambiente ou serviço apropriado;
- não usar `ARG` ou `ENV` de Docker para incorporar segredo no build;
- não imprimir configuração completa em logs;
- rotacionar imediatamente qualquer segredo exposto e analisar o histórico;
- separar credenciais de aplicação, migration e administração em produção.

Consulte o
[OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html).

## Dados pessoais e privacidade

- coletar somente dados com finalidade registrada;
- manter documento opcional até validação institucional;
- não usar dados pessoais reais em testes, seeds, screenshots ou Issues;
- mascarar dados em logs e evidências;
- restringir exportação e consulta por autorização e finalidade;
- não definir prazo de retenção sem validação institucional;
- evitar replicar dados pessoais em múltiplas tabelas ou artefatos.

## Backend e API

### Validação

- tratar todo dado HTTP como não confiável;
- validar tipo, formato, tamanho, faixa e relação entre campos;
- manter invariantes centrais no Domain;
- usar DTOs na borda da API quando existirem casos de uso;
- não confiar exclusivamente na validação do frontend;
- usar listas permitidas para enums, ordenação e filtros;
- limitar paginação, payload e tempo de execução.

Referência:
[OWASP Input Validation Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html).

### Erros

- não retornar stack trace, SQL, caminho local ou configuração em produção;
- padronizar erros de validação, autenticação, autorização e conflito;
- registrar detalhe técnico com correlation ID, não na resposta pública;
- não revelar se conta, documento ou registro sensível existe sem necessidade.

Referência:
[OWASP Error Handling Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Error_Handling_Cheat_Sheet.html).

### Autenticação e autorização

- contas devem ser individuais;
- senha deve ser armazenada somente com hash adequado e parâmetros revisados;
- negar por padrão e autorizar por política/caso de uso;
- validar permissão no servidor e no recurso solicitado;
- usuário inativo não pode autenticar ou manter sessão válida;
- manter access token curto e refresh token fora do alcance do JavaScript;
- persistir somente hash do refresh token e rotacioná-lo de forma atômica;
- impor inatividade e duração absoluta no servidor, sem sessão indefinida;
- proteger toda operação autenticada por cookie contra CSRF;
- revogar a família no logout, na desativação e na detecção de reutilização;
- exigir a credencial atual na troca autenticada, revogar todas as sessões e
  invalidar access tokens anteriores por uma versão de credencial verificada no servidor;
- não permitir que o Administrador escolha a senha permanente de outra pessoa;
- gerar credenciais iniciais e de redefinição com fonte criptográfica, prazo curto,
  uso único transacional, troca obrigatória, resposta sem cache e persistência somente do hash;
- restringir contas com credencial temporária ao fluxo mínimo de troca e logout;
- tratar redefinição como operação administrativa auditada e revogar todas as sessões;
- proteger operações administrativas e auditoria separadamente;
- não implementar algoritmo criptográfico próprio;
- não registrar senha, token ou header de autorização.

As decisões atuais de token, hash, bloqueio, ciclo administrativo de contas,
sessões e políticas estão em [Autenticação e autorização](authentication.md). A
Issue #190 implementa renovação rotativa, revogação no servidor e logout; a
Issue #191 integra esse contrato no frontend. A Issue #251 implementa a troca
autenticada e a Issue #258 implementa a fundação de credencial temporária e
redefinição administrativa sem inventar e-mail, SMS ou SSO. A decisão
institucional consolidada na Issue #220 atribui o procedimento a dois
Administradores funcionais e exige entrega direta da credencial temporária ao
titular. A observação desse procedimento e a matriz final de perfis permanecem
na homologação da Issue #162.

Referências:

- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html)
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [ASP.NET Core antiforgery](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

## Banco e Entity Framework Core

- usar queries parametrizadas e LINQ; nunca concatenar entrada em SQL;
- manter mapeamentos e constraints na Infrastructure;
- aplicar menor privilégio ao usuário da aplicação;
- não expor PostgreSQL publicamente em produção;
- revisar índices, exclusões e cascatas;
- usar transação para mudanças que precisam ser atômicas;
- não usar `EnsureCreated` em ambiente baseado em migrations;
- não aplicar migrations automaticamente no startup;
- nunca editar migration já aplicada em banco compartilhado;
- testar banco limpo, upgrade, downgrade e novo upgrade;
- exigir backup antes de migration de risco.

### Backup e recuperação

- tratar todo dump como dado confidencial, mesmo quando criado localmente;
- manter dumps fora do Git e não transmiti-los por canais pessoais;
- verificar nome, tamanho e SHA-256 pelo manifesto antes da restauração, sem
  tratar checksum como prova de autenticidade;
- validar o arquivo com uma restauração completa em banco isolado;
- nunca restaurar sobre o banco operacional por padrão;
- limpar bancos e arquivos temporários após o ensaio;
- não confundir o ensaio local da Issue #67 com backup protegido de produção;
- definir com a instituição retenção, criptografia, responsáveis, RPO e RTO antes
  da implantação real, conforme a Issue #30.
- registrar decisões e exercícios no
  [plano de retenção, backup e continuidade](../operations/data-retention-and-continuity.md).

O procedimento local reproduzível está em
[`infrastructure/database/README.md`](../../infrastructure/database/README.md).

## Auditoria e logs

Auditoria de negócio deve registrar quem, quando, qual entidade, qual registro e
a mudança necessária à rastreabilidade. Logs técnicos devem explicar a saúde da
aplicação. Um não substitui o outro.

- usar horário UTC e correlation ID;
- não registrar senha, token, connection string ou documento completo;
- proteger auditoria contra alteração indevida;
- definir comportamento quando a auditoria falhar;
- evitar log injection e dados fornecidos pelo usuário sem normalização;
- restringir acesso e retenção dos logs.

Estado implementado nas Issues #49 e #51: toda resposta possui correlation ID;
logs HTTP registram apenas método, template de rota, status e duração; exceções
inesperadas geram resposta `ProblemDetails` sem mensagem interna ou stack trace.
O método HTTP é projetado por lista permitida para um valor constante antes de
chegar ao log; métodos desconhecidos ou com caracteres de controle usam o rótulo
`<other-method>`, evitando injeção e cardinalidade controlada pelo cliente.
O middleware registra somente o tipo da exceção, nunca o objeto ou sua mensagem.
Entrada, saída, uso institucional e cadastro da frota geram auditoria de negócio
na mesma transação, com falha fechada e sem duplicar dados pessoais, placa,
identificação patrimonial ou itinerário. Leitura e gestão da frota usam políticas
separadas. Auditoria dos demais casos de uso e privilégios separados para outros
recursos continuam pendentes. A Issue #102 adiciona exportação OTLP opcional de
métricas e traces; collector, armazenamento, painéis, alertas e resposta
operacional ainda dependem da infraestrutura real.

Login bem-sucedido e bloqueio temporário de conta também usam falha fechada na
Issue #71: a alteração do usuário e a auditoria são persistidas juntas, e nenhum
token é emitido quando essa gravação falha. A trilha contém somente identificador,
resultado e, no bloqueio, sua expiração; não contém e-mail, credencial, hash,
token, endereço IP nem tentativa de usuário inexistente.

Desativação e reativação da Issue #73 também falham de forma fechada:
estado e auditoria são gravados na mesma transação, somente a transição
`active` é duplicada na trilha e mudanças concorrentes são serializadas para
preservar um Administrador ativo. A listagem não projeta hash, e toda requisição
com JWT confirma que conta e perfil continuam ativos.

Na Issue #76, criação administrativa e bootstrap passam a gerar auditoria na
mesma transação de pessoa e conta. Ações humanas exigem ator autenticado; o
bootstrap usa ator nulo e origem explícita de sistema, sem atribuir a ação ao
usuário que ainda está sendo criado. Nome, e-mail, senha e hash não são copiados.

Na Issue #78, a trilha passa a ser consultável sem acesso direto ao PostgreSQL.
A política dedicada `audits:read` começa restrita a Administrador, o período
padrão é de 30 dias, o máximo é de 90 dias e as páginas possuem até 100 eventos.
Filtros distinguem ator humano e origem de sistema. A resposta não faz joins com
pessoa, conta ou veículo e projeta somente os campos já persistidos na auditoria.
Justificativas de correção fazem parte da trilha e não devem receber dados
pessoais desnecessários. A leitura não gera outro evento neste recorte para evitar
recursão e ruído; essa decisão, a retenção e a matriz final ainda dependem de
validação institucional.

Na Issue #80, `SetorTransporte` recebe leitura do histórico geral por sua função
de supervisão e conferência, sem receber operação ou correção. O registro digital
deve substituir a planilha como fonte principal somente após homologação. Papel
fica restrito à contingência, com reconciliação posterior, evitando dois controles
permanentes e divergentes. O horário de fechamento não é codificado como bloqueio
fixo, pois existem exceções autorizadas e residentes.

Na Issue #271, o encerramento posterior de uma saída não registrada usa política
dedicada para Porteiro, Vigilante e Administrador; o Setor de Transporte continua
somente leitura. Motivo categorizado, observação e ator são obrigatórios. Um
horário observado só é persistido quando informado a partir de fonte confiável;
caso contrário, permanece nulo e não é apresentado como estimativa. O momento da
regularização é registrado separadamente. Registro e auditoria falham ou são
confirmados juntos, com bloqueio da linha contra concorrência; a auditoria não
duplica o texto livre da observação.

Na Issue #83, autorizações de eventos separam consulta operacional de gestão.
A decisão institucional registrada em setembro de 2026 e aplicada pela Issue
#270 mantém consulta para os quatro perfis, mas reserva criação, alteração e
cancelamento ao Administrador. Essa restrição é uma política do servidor, sem
nomes de pessoas codificados. Período e quantidade possuem limites, placas são
normalizadas e constraints do PostgreSQL impedem regras duplicadas. Evento,
regras e auditoria são persistidos atomicamente. A trilha não copia nome,
responsável, área, observação ou placa. A associação com entradas e o consumo
concorrente de cotas foram implementados posteriormente pela Issue #82.

Na Issue #82, a associação opcional passa a ser feita dentro da mesma transação da
entrada. O backend bloqueia a linha do evento, valida estado e janela, prioriza a
placa específica e depois a cota por tipo, e conta registros já associados antes
de persistir. A saída não reduz esse total. Essa serialização evita exceder a cota
em requisições concorrentes e o vínculo não pode ser alterado pela correção
descritiva. Identificadores do evento e da regra podem constar na auditoria; nome,
responsável, área, observação e placa não são duplicados.

Referência:
[OWASP Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html).

## Telemetria e observabilidade

- manter a exportação desabilitada até existir collector confiável;
- exportar somente sinais necessários e revisar atributos antes de cada nova
  instrumentação;
- não capturar corpo, token, credencial, connection string, SQL ou dados pessoais;
- manter a redação dos valores de query string obrigatória e revisar até os nomes
  dos parâmetros antes de criar novas rotas;
- excluir health checks dos traces para evitar ruído e custo artificial;
- usar TLS e autenticação para OTLP fora de rede local confiável;
- fornecer headers de autenticação somente por mecanismo de segredos;
- restringir acesso e definir retenção para métricas e traces;
- impedir que falha do collector derrube o fluxo operacional;
- validar cardinalidade, sampling, custo, painéis e alertas em homologação.

A Issue #102 instrumenta requisições ASP.NET Core e runtime e exporta métricas e
traces por OTLP somente quando `Observability:Enabled` está ativo. Logs continuam
fora desse exporter. Endpoints de saúde não geram traces. A configuração e o
roteiro de verificação estão no
[guia de observabilidade](../operations/observability.md).

## Limites e disponibilidade

- limitar requisições no servidor sem depender do frontend;
- usar partições independentes por identidade autenticada e, para tráfego anônimo,
  pelo endereço da conexão;
- aplicar limite específico ao login como complemento, não substituto, do bloqueio
  temporário de conta;
- rejeitar excesso sem fila e responder HTTP 429 em `ProblemDetails` correlacionado;
- manter liveness e readiness fora do rate limiting para os orquestradores;
- não confiar em `X-Forwarded-For` sem configurar uma lista de proxies conhecidos;
- calibrar limites definitivos com teste de carga e observabilidade em homologação;
- considerar rate limiting distribuído quando houver mais de uma instância da API.

O estado da Issue #69 usa janela fixa em memória por instância: 300 requisições
por minuto por usuário/IP na API e 30 por minuto por IP no login. Os valores são
configuráveis e inválidos impedem o startup.

Referência: [ASP.NET Core rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

## Frontend

- não considerar ocultação de botão como autorização;
- não embutir segredo no bundle;
- evitar conteúdo HTML não confiável;
- não persistir dado pessoal ou token sem decisão explícita;
- tratar mensagens de erro sem expor detalhes internos;
- usar dependências bloqueadas e auditar atualizações;
- manter proteção contra clickjacking, MIME sniffing e políticas de conteúdo no
  servidor/proxy quando a implantação for definida.

O Nginx do frontend aplica uma Content Security Policy same-origin, bloqueia
incorporação em frames, impede MIME sniffing, reduz informações de referência e
nega câmera, microfone, geolocalização, pagamentos e USB por padrão. A CI verifica
esses cabeçalhos tanto na aplicação quanto em uma resposta de erro do proxy. Uma
necessidade futura de câmera deve passar por nova análise de ameaça e liberar
somente a permissão e a origem estritamente necessárias. HSTS pertence ao
terminador HTTPS de homologação ou produção e não é anunciado pelo servidor HTTP
local.

## Containers

- usar build em múltiplos estágios;
- copiar somente artefatos necessários;
- executar como usuário não privilegiado;
- manter `.dockerignore`;
- não incluir segredos em camada, argumento ou imagem;
- fixar e atualizar imagens de forma controlada;
- reduzir pacotes, capabilities e superfícies desnecessárias;
- separar liveness e readiness;
- analisar vulnerabilidades na CI;
- não publicar PostgreSQL em produção sem necessidade e controle de rede.

O hardening é rastreado pela Issue #25.

## Dependências e cadeia de suprimentos

- manter lockfiles quando suportados;
- usar instalação determinística na CI;
- habilitar atualização automatizada por ecossistema;
- executar auditoria de vulnerabilidades;
- revisar dependência nova quanto a manutenção, licença e necessidade;
- limitar permissões de workflows;
- usar versões controladas de Actions e imagens;
- não executar código de origem não confiável com segredo disponível.

## Testes de segurança

Cada controle deve possuir teste proporcional ao risco:

- entrada inválida e limites;
- autenticação válida, inválida, bloqueada e inativa;
- autorização permitida e negada por perfil/recurso;
- ausência de segredo/dado pessoal em resposta e log;
- constraints e transações no PostgreSQL real;
- migrations e recuperação;
- indisponibilidade de dependências;
- build e execução dos containers;
- auditoria de ações e tentativas relevantes.

Provider EF InMemory não substitui testes de integração PostgreSQL.

## Pull Request seguro

Antes de aprovar:

- [ ] Issue e ameaça relacionadas;
- [ ] nenhuma credencial ou dado pessoal real;
- [ ] validação no servidor;
- [ ] autorização por recurso/operação;
- [ ] erros sem detalhes internos;
- [ ] logs e auditoria revisados;
- [ ] dependências justificadas e auditadas;
- [ ] migrations e rollback testados;
- [ ] testes negativos incluídos;
- [ ] CI aprovada;
- [ ] README, Wiki e modelo de ameaças atualizados quando aplicável.

## Vulnerabilidade ou incidente

1. Não publicar detalhes exploráveis em Issue pública antes da contenção.
2. Preservar evidências e identificar versões afetadas.
3. Revogar/rotacionar segredos quando houver possibilidade de exposição.
4. Conter o componente afetado.
5. Corrigir em branch restrita quando necessário.
6. Validar regressão e dependências relacionadas.
7. Comunicar responsáveis institucionais.
8. Registrar causa, impacto, recuperação e prevenção.

## Pendências rastreadas

- #25 — hardening de CI/CD e containers;
- #29 — autenticação e autorização;
- #30 — retenção, backup, recuperação e contingência;
- #31 — validação, erros, logs e auditoria da API;
- #69 — rate limiting correlacionado da API;
- #71 — auditoria transacional de login e bloqueio de conta;
- #73 — ciclo administrativo seguro de contas;
- #76 — auditoria de criação e bootstrap de contas;
- #78 — consulta administrativa segura da trilha de auditoria;
- #67 — backup e restauração verificável do PostgreSQL local.
