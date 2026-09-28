# Frontend

Frontend do sistema Controle de Acesso de Veículos do IFPE – Campus Belo Jardim.

## Estado atual

A versão atual integra autenticação e sessão em memória ao endpoint
`POST /auth/login`. O fluxo geral de veículos também consome a API para registrar
entrada, listar acessos em aberto, registrar saída e consultar o histórico. O
catálogo de frota também lista, cria, edita e desativa veículos institucionais;
o catálogo de motoristas lista, autoriza e desativa autorizações pela API. A
área de eventos também consulta, cria, edita e cancela autorizações antecipadas.
A área de utilizações institucionais registra saídas e retornos e consulta usos
abertos e histórico conforme o perfil autenticado.
A visão geral consulta o resumo operacional diário agregado da API. A área de
administração consulta, cria, desativa e reativa contas pela API, mostra o
estado mínimo das credenciais temporárias e permite ao Administrador redefinir
a credencial de outra conta ativa. Credenciais emitidas pelo servidor aparecem
uma única vez em memória, com cópia explícita e sem persistência no navegador. A
mesma tela também consulta a trilha de auditoria em uma área separada.

Fluxo sugerido para validação local:

1. iniciar a API e acessar `/login` com uma conta individual local;
2. confirmar que e-mail e perfil exibidos vieram da resposta da API;
3. como Porteiro ou Vigilante, registrar entradas consecutivas em
   `/acessos/novo` e confirmar que a ação de continuidade reinicia o formulário
   com foco na placa; conferir também a associação opcional com uma autorização
   de evento vigente, sem impedir uma entrada comum quando a consulta falhar;
4. localizar o veículo em `/acessos/abertos` e registrar a saída;
5. como Transporte, consultar eventos e manter os catálogos ativos da frota e de
   motoristas;
6. como Administrador, manter as autorizações de eventos, filtrar o histórico,
   gerenciar contas fictícias e consultar a trilha de auditoria em
   `/administracao`.

Porteiro e Vigilante possuem a mesma navegação operacional. O Setor de Transporte
mantém os catálogos da frota e consulta eventos na área de supervisão. O
Administrador gerencia contas, frota e eventos e possui o acesso operacional
excepcional permitido pelo backend, embora entrada e saída continuem ocultas de
seu menu rotineiro.

A navegação apresenta `Operações` e `Consultas de apoio` para Porteiro e
Vigilante; `Supervisão` e `Gestão` para o Setor de Transporte; e `Supervisão`,
`Consultas de apoio` e `Gestão técnica` para o Administrador. A matriz visual,
as rotas permitidas e as capacidades de cada página possuem uma fonte
centralizada em `src/routes/routeMetadata.ts`. Um item oculto não representa
negação de autorização: o frontend ainda protege o acesso direto conforme o
perfil autenticado, e o backend permanece como autoridade final em cada
requisição. Os nomes e agrupamentos são hipóteses reversíveis do MVP; sua
encontrabilidade e aceitação institucional continuam na Issue #162.

O formulário integrado segue o fluxo geral documentado: nome do condutor,
placa, objetivo e categoria são obrigatórios; tipo do veículo e observação são
opcionais. Categoria e objetivo permanecem distintos. Documento não é exigido
por decisão apenas visual, e horário, autorização e duplicidade continuam sob
responsabilidade da API.

Para reduzir digitação na portaria, o formulário apresenta objetivos rápidos e
tipos de veículo predefinidos, mantendo “Outro” com texto livre. O valor final é
enviado nos mesmos campos `objective` e `vehicleType` do contrato atual. As
opções de objetivo são hipóteses do MVP e ainda dependem da observação e
homologação institucional da Issue #162; elas não estabelecem regras entre
categoria, objetivo ou tipo de veículo.

Uma busca opcional por placa ou nome permite recuperar pares recorrentes de
veículo e condutor pelo endpoint `GET /access-records/entry-candidates`. A
consulta começa com três caracteres, não persiste pesquisas nem resultados e
mantém o preenchimento manual disponível em falhas ou resultados vazios. Quando
um candidato é escolhido, `vehicleId` e `personId` são enviados juntos; qualquer
edição manual da placa ou do condutor remove esse vínculo antes do envio. A
seleção reduz digitação, mas não autoriza a entrada: o backend revalida o par e
usa os dados canônicos. A utilidade e a linguagem desse atalho ainda dependem da
homologação institucional da Issue #162. Iniciar outra pesquisa mantém o
candidato atual até que outro resultado seja escolhido. O retorno explícito ao
preenchimento manual limpa placa, condutor e tipo derivados, preservando
categoria, objetivo, evento e observação.

A lista de acessos em aberto usa linhas compactas no desktop e cartões compactos
no mobile. O tempo transcorrido é somente informativo e permanece separado do
horário da última resposta bem-sucedida da API. A atualização da lista é manual;
não há polling ou classificação de atraso. O volume real representativo de pico
e a aceitação institucional desse formato permanecem para a Issue #162.

Porteiro, Vigilante e Administrador podem regularizar excepcionalmente uma saída
que não foi registrada no momento real. Essa ação permanece secundária à saída
normal e exige motivo e observação. O horário observado é opcional e só deve ser
informado quando existir fonte confiável; quando estiver vazio, o frontend envia
`observedExitAtUtc: null` e não cria estimativas. O histórico distingue a saída
normal, a saída excepcional observada, o horário de saída desconhecido e o
momento posterior da regularização. O Setor de Transporte permanece somente para
consulta, e a visibilidade da interface não substitui a autorização da API.

Quando necessário, o operador pode abrir a seção opcional de eventos, consultar
as autorizações ativas e vigentes e escolher explicitamente uma delas. A interface
mostra período, área, responsável e saldo das regras, mas não pré-seleciona um
evento nem decide se a placa, o tipo, a janela ou a cota permitem a entrada. Essa
validação permanece transacional no backend. Falha ao consultar eventos não
bloqueia o fluxo geral; conflitos retornados ao registrar preservam o formulário
para conferência. O histórico identifica pelo nome somente os acessos que foram
associados a uma autorização.

No histórico geral, Porteiro, Vigilante e Administrador podem abrir uma correção
auditada para alterar somente objetivo, categoria e observação, sempre com uma
justificativa obrigatória. Placa, condutor, horários, situação, autoria original
e eventual associação com evento permanecem como contexto textual imutável. A
interface usa a resposta canônica de `PUT /access-records/{id}/correction` e
revalida o histórico com os filtros efetivamente aplicados. A página atual é
preservada enquanto continuar válida; caso deixe de existir, a última página
válida é carregada. O Setor de Transporte continua somente leitura, e a
autorização efetiva permanece sob responsabilidade do backend.

Após uma resposta bem-sucedida, o operador pode continuar na mesma tela para
registrar o próximo veículo ou abrir a lista de acessos. A continuidade limpa os
dados concluídos, anuncia o sucesso e restaura o foco na placa; falhas preservam
os valores para correção. Esse atalho é uma hipótese de UX para atendimento em
horário de pico e ainda depende da homologação da Issue #162.

Saída, retorno, quilometragem e motorista de veículos institucionais pertencem ao
fluxo próprio da frota. Previsão de permanência, alertas baseados em prazo e
descarte por período não são apresentados enquanto seus contratos e políticas
institucionais permanecerem pendentes.

O catálogo integrado usa `GET`, `POST`, `PUT` e `DELETE` em
`/institutional-vehicles`. Porteiro e Vigilante apenas consultam; Setor de
Transporte e Administrador podem criar, editar e desativar. A lista da API contém
somente veículos ativos. Embora o backend possua uma operação de reativação, o
frontend não oferece essa ação enquanto não existir contrato para consultar os
veículos inativos. A desativação preserva viagens e histórico no servidor.

O catálogo de motoristas usa `GET`, `POST` e `DELETE` em
`/institutional-drivers`. Os quatro perfis autenticados consultam os motoristas
ativos; somente Setor de Transporte e Administrador autorizam ou desativam. Nome
é obrigatório. Tipo e número do documento são opcionais, mas devem ser enviados
juntos; esses dados não retornam no catálogo nem são mantidos pela interface. O
backend não oferece edição ou consulta de autorizações inativas, portanto o
frontend não inventa essas operações.

Autorizações de eventos usam `GET`, `POST`, `PUT` e `DELETE` em
`/event-authorizations`. Os quatro perfis autenticados consultam; somente o
Administrador cria, edita e cancela. A visibilidade das ações na interface reduz
ações enganosas, mas não substitui a política de autorização aplicada pela API.
A interface separa a vigência da autorização, a quantidade prevista nas regras,
as entradas já consumidas e a quantidade restante. Placa específica representa
exatamente um veículo; cota por tipo pode representar de 1 a 1000 veículos.
Cancelamento é lógico, exige confirmação e não é descrito como exclusão do
histórico.

Os períodos são preenchidos com controles nativos de data e hora e possuem uma
instrução textual associada. Essa escolha evita introduzir um calendário
customizado sem suporte de teclado comprovado e segue as recomendações de
descrição de formato do
[W3C APG](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/examples/datepicker-dialog/)
e os testes de intervalo do
[U.S. Web Design System](https://designsystem.digital.gov/components/date-range-picker/accessibility-tests/).

O login autentica e as rotas são filtradas pela identidade devolvida pela API.
Isso não transforma a interface em controle de autorização: o backend continua
validando cada operação. Falhas de validação, rede, conflito e acesso negado são
apresentadas sem produzir confirmação falsa. A integração não representa
homologação para produção.

## Sessão e segurança

O access token permanece exclusivamente na memória do processo JavaScript. O
refresh token é controlado pelo servidor em cookie `HttpOnly`, não integra o JSON
e não pode ser lido pelo frontend. O fluxo implementado:

- tenta restaurar a sessão de forma controlada antes de exibir conteúdo protegido;
- solicita um token antifalsificação novo em `GET /auth/csrf` antes de renovar ou
  encerrar a sessão;
- envia cookies somente pelo cliente same-origin e usa o proxy local para preservar
  o caminho `/api/auth` no navegador;
- compartilha uma única renovação em andamento entre requisições concorrentes e
  permite no máximo uma repetição depois de HTTP 401;
- nunca repete login, refresh, logout, falhas de rede ou uma requisição já repetida;
- mantém formulários renderizados durante uma falha transitória de renovação, até
  a expiração efetiva do access token;
- encerra imediatamente o estado local no logout e tenta revogar a sessão no
  servidor, informando quando essa confirmação não for possível;
- permite que qualquer perfil autenticado altere a própria senha por
  `POST /auth/password`, sem repetir automaticamente essa mutação;
- encerra o estado local depois da troca de senha bem-sucedida e solicita uma
  nova entrada, pois o servidor revoga as sessões anteriores;
- valida obrigatoriamente `user.requiresPasswordChange` no login e na
  renovação; uma resposta sem esse booleano é rejeitada em vez de liberar o
  painel por padrão;
- mantém o indicador de troca obrigatória somente na sessão em memória e,
  quando ativo, exibe apenas a definição da senha permanente e o logout;
- redireciona qualquer rota operacional digitada durante o primeiro acesso para
  a troca obrigatória, sem renderizar menus, painéis ou ações de negócio;
- usa bloqueio exclusivo do navegador para coordenar renovações entre abas e
  comunica somente eventos de encerramento e metadados temporais de atividade,
  sem transmitir tokens, credenciais, e-mail ou identidade;
- usa `X-Session-Server-Time` como referência comum para validar e converter em
  durações relativas os prazos de inatividade e duração absoluta publicados nos
  cabeçalhos de login e renovação; respostas ausentes, inválidas ou incoerentes
  são rejeitadas sem depender de o relógio do tablet estar adiantado ou atrasado;
- considera atividade somente uma interação humana real por teclado, ponteiro,
  toque ou clique confiável de tecnologia assistiva; movimento do mouse, foco,
  mudança de aba, timers, renderização, HTTP e renovação automática não reiniciam
  o período local;
- encerra a sessão após 15 minutos sem atividade ou ao atingir o limite absoluto,
  limpa imediatamente token e identidade da memória, coordena o bloqueio entre
  abas e tenta revogar a família no servidor sem manter a interface desbloqueada;
- verifica novamente os prazos antes de qualquer renovação automática ou causada
  por HTTP 401, no retorno do background e depois de suspensão do dispositivo;
- quando uma renovação vence com a aba em segundo plano, mantém somente em
  memória a indicação pendente; retornar à aba não renova a sessão sozinho, e a
  primeira atividade humana confiável solicita no máximo uma renovação;
- diferencia o vencimento do access token do encerramento da sessão: um token
  vencido é removido da memória, mas o monitor e a continuidade permanecem
  válidos até o limite de inatividade ou o limite absoluto;
- ao retomar, reconcilia o prazo humano temporal salvo por outra aba sem ampliar
  o limite absoluto conhecido, cobrindo mensagens entre abas perdidas durante a
  suspensão;
- trata alteração regressiva do relógio de forma conservadora e ignora uma
  resposta de renovação que chegue depois do encerramento da sessão.

Para impedir que o descarte e a restauração de uma aba recuperem uma sessão após
o último prazo humano aceito, o frontend mantém no `localStorage` somente um
registro versionado com prazos e observação temporal. Esse metadado não contém
token, credencial, e-mail, perfil, identificador ou outro dado pessoal, é removido
no encerramento da sessão — não no simples vencimento do access token — e nunca
concede autorização. Sua ausência, expiração,
corrupção ou indício de regressão do relógio bloqueia a renovação; sessão,
revogação e limite absoluto continuam sendo impostos pelo servidor.

Nenhum token é colocado em URL, estado de rota, log, `localStorage` ou
`sessionStorage`. O backend usa resposta 401 genérica para credencial incorreta,
conta inativa e bloqueio temporário; o frontend preserva essa indistinguibilidade
para não ajudar na enumeração de contas. Respostas 403 continuam representando
acesso negado, e toda autorização efetiva permanece no backend.

A coordenação entre abas depende da Web Locks API. O navegador-alvo Brave/Chromium
possui esse recurso; em um navegador sem suporte, a restauração e a renovação
falham de modo seguro e a pessoa é orientada a entrar novamente, sem persistir ou
compartilhar credenciais como alternativa.

A política de 15 minutos e o limite absoluto de 12 horas pertencem à Issue #268.
A validação no tablet compartilhado da Portaria continua necessária antes de
considerar o comportamento homologado institucionalmente. O logout explícito
permanece obrigatório na troca de operador.

## Tecnologias

- React;
- TypeScript;
- Vite;
- Tailwind CSS 4;
- React Router;
- Axios;
- ESLint;
- Vitest e Testing Library;
- axe-core para auditoria automatizada de acessibilidade.

## Paleta tipográfica

O frontend utiliza dois tons semânticos compartilhados para manter a linguagem
visual consistente entre as páginas:

- `ink` (`#004953`) identifica textos principais, títulos, controles e foco;
- `ink-soft` (`#1A615D`) identifica descrições, metadados e textos de apoio.

Os dois tons mantêm contraste superior a 4,5:1 sobre branco e sobre o fundo
creme do sistema. Em superfícies coloridas, o componente deve usar a combinação
com contraste medido, sem substituir as cores semânticas próprias de erro,
sucesso, aviso ou indisponibilidade.

## Testes e acessibilidade

### Transições de rota

Os títulos das páginas e as permissões necessárias para identificar um acesso
negado ficam centralizados em `src/routes/routeMetadata.ts`. O componente
`RouteTransitionManager` reage somente a mudanças de caminho, atualiza o título
do documento e move o foco para o alvo definido pela rota. O login usa o campo
de e-mail para permitir o início imediato da autenticação; as demais telas usam
o primeiro `h1` dentro do conteúdo principal. Esse título recebe
`tabIndex="-1"` programaticamente, portanto não entra na sequência normal de Tab.

Novas rotas devem receber um título descritivo no mapa e renderizar exatamente
um `h1` dentro de `main`. Filtros, paginação, retry, abertura de formulários e
outras atualizações internas não mudam o caminho e, por isso, não deslocam o
foco. O mecanismo não cria uma região `aria-live`; o novo contexto é comunicado
pelo título do documento e pelo foco no cabeçalho, preservando o skip link e a
gestão de foco do menu móvel.

A suíte usa Vitest com JSDOM e Testing Library. Os testes consultam elementos por
papel e nome acessível e cobrem autenticação, restrição visual por perfil,
movimentações gerais, estados de carregamento, vazio, falha e acesso negado.
O resumo operacional possui cobertura de contrato, perfis, seleção de data,
falha com nova tentativa e resposta diária sem movimentações.
A administração de contas possui cobertura de contrato, filtros, criação sem
senha definida pelo Administrador, exibição única e cópia de credencial
temporária, redefinição categorizada, mudanças de estado, conflitos, rate
limiting, falha de recarga e acessibilidade.
A auditoria administrativa possui cobertura de contrato, restrição por perfil,
filtros locais e da API, paginação, estados vazio e indisponível, nova tentativa
e renderização segura de detalhes e estados JSON.

As auditorias com axe-core abrangem login, layout autenticado, registro de
entrada, acessos em aberto e histórico. Violações classificadas como sérias ou
críticas fazem os testes falharem. O menu móvel também possui testes de abertura,
fechamento por `Escape`, contenção do foco por teclado e devolução do foco ao
botão que abriu o diálogo.

Na pasta `src/frontend`, execute:

```powershell
npm test
```

A CI executa instalação bloqueada, lint, testes e build em toda alteração do
frontend. Os testes são determinísticos, usam somente dados fictícios e não
dependem da API ou de acesso à rede.

A auditoria automatizada não comprova conformidade completa com a WCAG nem
substitui testes com usuários. O JSDOM não calcula layout e contraste como um
navegador real; por isso, as regras `color-contrast` e `link-in-text-block` são
desativadas apenas nessa auditoria e continuam exigindo revisão manual em
aproximadamente 390 px e 1440 px. Essa limitação é documentada pelo
[axe-core](https://github.com/dequelabs/axe-core/tree/develop/doc/examples/jest_react),
e o gerenciamento do foco do menu segue o
[padrão de diálogo modal do W3C](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/).

## Executar localmente

Na pasta `src/frontend`:

```powershell
npm ci
npm run dev
```

## Variáveis de ambiente

Por padrão, o cliente usa o caminho de mesma origem `/api`. Durante o
desenvolvimento, o Vite encaminha esse caminho para a API local em
`http://127.0.0.1:5118` e remove o prefixo `/api` antes de a requisição chegar
ao backend.

Crie um arquivo `.env.local` somente quando precisar substituir a URL usada pelo
cliente:

```env
VITE_API_BASE_URL=/api
```

Se a variável não estiver definida, o cliente HTTP utilizará:

```text
/api
```

No ambiente Docker, o Nginx aplica o mesmo contrato e encaminha `/api/*` para o
container backend. Essa estratégia evita expor uma segunda origem ao navegador e
dispensa uma política CORS ampla no MVP.

Autenticação, movimentações gerais, utilizações da frota, autorizações de eventos
e catálogos institucionais utilizam o cliente HTTP centralizado. Não inclua
tokens, senhas ou credenciais em variáveis expostas ao frontend.

## Estrutura de diretórios

```text
src/
├── components/
│   ├── layout/       # Layouts compartilhados.
│   └── ui/           # Componentes visuais reutilizáveis.
├── demo/             # Estado e dados exclusivamente demonstrativos.
├── features/
│   ├── access-records/ # Contratos, validação e serviços do fluxo geral.
│   ├── audit-trail/ # Consulta administrativa e segura da trilha de auditoria.
│   ├── authentication/ # Formulário, service, sessão e tipos de autenticação.
│   ├── event-authorizations/ # Consulta e manutenção de autorizações de eventos.
│   ├── institutional-drivers/ # Contratos, formulário e catálogo de motoristas.
│   ├── institutional-usages/ # Saídas, retornos e histórico da frota institucional.
│   ├── institutional-vehicles/ # Contratos, formulário e serviços da frota.
│   ├── operational-summary/ # Contrato, consulta e apresentação do resumo diário.
│   └── user-accounts/ # Consulta e ciclo administrativo de contas.
├── pages/            # Componentes associados às páginas.
├── routes/           # Configuração central das rotas.
├── services/         # Cliente HTTP e integrações externas.
├── hooks/            # Hooks reutilizáveis.
├── types/            # Tipos compartilhados.
├── utils/            # Funções utilitárias puras.
├── App.tsx           # Composição principal.
├── main.tsx          # Ponto de entrada do React.
└── index.css         # Tokens, estilos globais e Tailwind.
```

## Convenções

- Use TypeScript em todo código novo.
- Use Tailwind CSS 4 para estilização.
- Mantenha componentes com uma responsabilidade clara.
- Não coloque regras de negócio em componentes visuais.
- Não faça chamadas HTTP diretamente em componentes de apresentação.
- Centralize futuras chamadas HTTP em `services/`.
- Use PascalCase para componentes React.
- Use branches específicas para cada alteração.
- Não adicione bibliotecas sem justificar sua necessidade.

## Limites atuais

- a troca obrigatória no primeiro acesso depende da integração coordenada com o
  contrato de credencial temporária do backend no PR #259;
- recuperação autônoma de senha sem sessão;
- definição institucional do canal usado para repassar credenciais temporárias
  criadas ou redefinidas pelo Administrador;
- suporte à renovação transparente em navegadores sem Web Locks API;
- persistência dos dados demonstrativos;
- integração com PostgreSQL;
- garantia de autorização baseada somente na interface;
- homologação do cliente ou prontidão para produção.

## Validação

Na pasta `src/frontend`:

```powershell
npm run lint
npm run build
npm test
```
