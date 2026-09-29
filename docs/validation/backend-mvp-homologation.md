# Homologação integrada do MVP

**Status:** direção funcional do MVP aceita; validação observada com os quatro perfis ainda pendente<br>
**Escopo:** frontend, backend, PostgreSQL, infraestrutura local, UX e QA<br>
**Rastreabilidade:** Issues #88, #108, #112, #162, #167, #175, #176, #177, #178 e #180; PRs #164, #168, #170, #183, #184, #185, #186 e #187

Este documento transforma as funcionalidades já integradas à `main` em uma
sessão reproduzível de validação. Ele não comprova aceitação institucional e não
autoriza uso em produção. O objetivo é apresentar o comportamento existente,
registrar o entendimento dos usuários e converter ajustes aprovados em issues
separadas.

## 1. Regras da sessão

- Utilize somente nomes, documentos, placas, e-mails e itinerários fictícios.
- Não projete `.env`, connection strings, senhas, tokens ou logs durante a reunião.
- Não execute a sessão em banco que contenha dados reais.
- Não altere regras ou código durante a demonstração.
- Classifique cada retorno como `Aceito`, `Ajustar`, `Rejeitado` ou `Pendente`.
- Registre uma nova issue para cada mudança aprovada; não amplie a Issue #88.
- Trate jornada de 12 horas e fechamento por volta das 23h como contexto
  operacional, não como bloqueios automáticos já aprovados.

O arquivo-modelo
[`ControleAcessoVeiculos.Homologation.http`](../../src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.Homologation.http)
contém as requisições na ordem recomendada. Os identificadores retornados e os
tokens devem existir somente em uma cópia local ignorada pelo Git:

```powershell
Copy-Item `
  src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.Homologation.http `
  src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.Homologation.local.http
```

Execute e edite apenas `ControleAcessoVeiculos.Homologation.local.http`. O padrão
`*.local.http` está no `.gitignore`; confirme mesmo assim com `git status` antes
de cada commit.

## 2. Participantes e responsabilidades

| Participante        | O que deve validar                                                                         |
| ------------------- | ------------------------------------------------------------------------------------------ |
| Porteiro            | rapidez do registro, dados necessários, consulta de abertos, saída e continuidade do turno |
| Vigilante           | substituição do Porteiro, consulta e correção descritiva justificada                       |
| Setor de Transporte | supervisão, históricos, frota, motoristas, eventos e resumo diário                         |
| Administrador       | contas, trilha de auditoria e acesso excepcional                                           |
| Equipe              | comportamento técnico, evidências, dúvidas e decisões sem induzir respostas                |

Uma pessoa pode representar mais de um papel na reunião, mas a avaliação deve
continuar separada por perfil.

## 3. Preparação do ambiente

### 3.1. Pré-condições

- branch `main` sincronizada e árvore de trabalho limpa;
- .NET SDK 10 e `dotnet-ef` 10;
- Docker Desktop ou Docker Engine com Compose;
- arquivo `infrastructure/docker/.env` criado a partir de `.env.example`, fora do Git;
- senha local do PostgreSQL e chave JWT diferentes dos marcadores do exemplo;
- banco local exclusivo para desenvolvimento ou homologação;
- nenhuma informação institucional real no ambiente.

Não use `docker compose down --volumes` para “limpar” um ambiente sem confirmar
explicitamente que o volume pode ser descartado. Quando for necessário um banco
novo, crie um ambiente local separado e documente a decisão.

### 3.2. Validação técnica anterior

Execute a partir da raiz:

```powershell
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet format src/backend/ControleAcessoVeiculos.slnx --no-restore --verify-no-changes
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet test src/backend/ControleAcessoVeiculos.slnx --no-build --no-restore
git diff --check

Set-Location infrastructure/docker
docker compose config
docker compose build backend
docker compose up -d postgresql
Set-Location ../..

Set-Location src/frontend
npm ci
npm test -- --run
npm run lint
npm run build
Set-Location ../..
```

Resultado esperado: formatação, builds e todas as suítes sem erros, além do
PostgreSQL saudável. Os testes de integração exigem acesso ao Docker porque usam
PostgreSQL real e descartável via Testcontainers. Registre no relatório a
contagem efetivamente observada em cada suíte e o SHA demonstrado; não copie uma
contagem histórica como evidência sem executar os comandos.

### 3.3. Migrations e administrador inicial

Configure `ConnectionStrings__DefaultConnection` e
`Authentication__Jwt__SigningKey` no terminal local, com os mesmos valores do
ambiente de demonstração, sem imprimi-los. Aplique as migrations:

```powershell
dotnet ef database update `
  --project src/backend/ControleAcessoVeiculos.Infrastructure `
  --startup-project src/backend/ControleAcessoVeiculos.API
```

Se o banco ainda não possuir usuários, configure temporariamente
`BootstrapAdmin__Name`, `BootstrapAdmin__Email` e
`BootstrapAdmin__Password`, execute o provisionamento e remova as variáveis do
processo:

```powershell
dotnet run --project src/backend/ControleAcessoVeiculos.API -- --bootstrap-admin
Remove-Item Env:BootstrapAdmin__Name, Env:BootstrapAdmin__Email, Env:BootstrapAdmin__Password
```

O bootstrap deve informar que criou o primeiro Administrador. Se já houver
usuários, ele deve recusar uma nova inicialização. Nunca registre a senha no
documento, terminal compartilhado, gravação ou captura de tela.

### 3.4. Inicialização da API

Para executar localmente:

```powershell
dotnet run --project src/backend/ControleAcessoVeiculos.API --launch-profile http
```

Confirme em outro terminal:

```powershell
Invoke-RestMethod http://localhost:5118/health/ready
```

Resultado esperado: `status` igual a `Healthy`. Para usar a API pelo Compose,
substitua a URL do arquivo HTTP por `http://localhost:8080` e confirme primeiro
que as migrations já foram aplicadas.

### 3.5. Dados fictícios representativos

Para preparar previamente contas e cenários visíveis na demonstração, execute em
outro terminal:

```powershell
./infrastructure/demo/Initialize-DemoData.ps1 `
  -ApiBaseUrl http://127.0.0.1:5118
```

O script aceita somente uma API em loopback e solicita interativamente a conta
administrativa e três senhas locais de demonstração distintas. Ele usa os endpoints normais
para preservar autorização, validações e auditoria e deixa exemplos abertos e
encerrados. Não projete os prompts durante a reunião. Em uma segunda execução,
informe as mesmas senhas para comprovar que os dados não são duplicados.

A CI repete automaticamente esse preparo em infraestrutura descartável sempre
que seus contratos técnicos mudam. Mesmo com o workflow aprovado, execute este
roteiro manual durante a preparação da sessão: automação detecta regressões, mas
não observa compreensão, esforço, dúvidas ou adequação do fluxo aos usuários.

José Ernandes é o responsável técnico provisório pelo ambiente local enquanto
mantiver vínculo com o campus. Isso não o torna autoridade para aprovar política
institucional de retenção, proteção de dados ou produção. Antes da transferência
do projeto, o responsável sucessor e o procedimento de entrega devem ser
registrados.

### 3.6. Inicialização e conferência do frontend

Com a API disponível em `http://127.0.0.1:5118`, execute em outro terminal:

```powershell
Set-Location src/frontend
npm run dev
```

Abra o endereço informado pelo Vite, normalmente `http://localhost:5173`. O
proxy de desenvolvimento encaminha `/api` para a API local; não exponha token ou
credencial na URL, em capturas ou nas ferramentas do navegador.

Antes da chegada dos participantes:

- autentique uma vez com cada perfil usando somente contas fictícias;
- confirme que o menu muda conforme o perfil e que uma rota incompatível exibe
  acesso negado;
- teste aproximadamente 390 px e 1440 px de largura, sem classificar a adaptação
  como aprovada antes da sessão;
- percorra ao menos um cenário apenas com teclado, observando foco visível,
  ordem de navegação e retorno do foco;
- confira carregamento, lista vazia e falha controlada sem desligar serviços que
  possam conter dados ou trabalho de outra pessoa;
- registre o SHA de `main`, navegador, sistema operacional e resolução usados.

Se o Docker, a API, o banco ou o frontend não estiverem disponíveis, interrompa
a sessão e registre uma falha de ambiente. Não peça ao participante para avaliar
uma tela parcialmente carregada como se fosse o produto funcionando.

### 3.7. Ensaio técnico local registrado em 9 de setembro de 2026

Este ensaio preparou a sessão, mas não substitui a participação dos quatro
perfis exigida pela Issue #162:

- a `main` contém o merge `ba423b3` do PR #168 e mantém o PR #170 incorporado;
- PostgreSQL ficou saudável, as 12 migrations foram aplicadas, a API respondeu
  em `http://localhost:8081/health/ready` e o frontend em
  `http://localhost:3000` no ambiente Compose local configurado;
- o Administrador inicial e as contas fictícias de Porteiro, Vigilante e Setor
  de Transporte foram provisionados por entrada protegida, sem registrar
  credenciais;
- o inicializador criou o conjunto fictício esperado. A repetição manual para
  comprovar idempotência não foi executada; essa propriedade continua coberta
  pelo workflow `CI - Demo data`;
- uma sessão técnica com a conta fictícia de Porteiro validou os dois caminhos
  de registro, uma falha controlada, navegação por teclado e as larguras
  390 × 844 e 1440 × 1000, sem overflow horizontal;
- uma sessão técnica com a conta fictícia de Vigilante confirmou registro e
  encerramento de acesso geral, consulta do histórico, permissões de consulta
  sobre os cadastros institucionais e o ciclo de saída e retorno de veículo
  institucional. Os registros usados foram exclusivamente fictícios;
- a conta fictícia do Setor de Transporte foi verificada em navegador Brave
  controlado. Os históricos geral e institucional e os catálogos de frota,
  motoristas e eventos responderam com sucesso; os formulários de manutenção
  foram abertos e cancelados sem mutações. O menu não expôs entrada, acessos em
  aberto ou administração, e a tentativa de abrir diretamente a rota de nova
  entrada resultou em acesso negado;
- a conta fictícia de Administrador carregou quatro contas e permitiu filtrar a
  listagem. Ao alternar para a trilha de auditoria e voltar, filtro e resultado
  permaneceram coerentes sem nova consulta inicial. O formulário exibiu a
  terminologia neutra e o campo de senha protegido e foi cancelado sem envio. A
  rota operacional excepcional permaneceu acessível diretamente, embora não
  apareça no menu rotineiro, conforme o contrato atual de autorização;
- nas verificações controladas de Transporte e Administrador, somente consultas
  `GET` foram observadas e todas responderam com HTTP 200; nenhuma mutação foi
  enviada;
- a suíte da `main` possui 134 testes frontend em 21 arquivos.

Ainda faltam sessões com representantes institucionais adequados dos quatro
perfis, além de um volume de pico combinado com a Portaria e a Vigilância. As
execuções acima foram realizadas como ensaio técnico com contas fictícias pela
equipe do projeto; não comprovam compreensão, utilidade ou aprovação pelos
futuros usuários. Por isso, o resultado permanece classificado como preparação
técnica, e não como homologação ou autorização para produção.

### 3.8. Preparação técnica atual em 12 de setembro de 2026

Esta preparação atualiza o roteiro para a `main` no SHA
`a7065d336a224635321ad50736742831b4aed203`, que incorpora os PRs #183, #184,
#185, #186 e #187. Ela foi executada no ambiente local Windows com Docker
Desktop e Brave 153.1.95.101 disponível. Não houve nesta data uma nova sessão
observada com representantes institucionais.

Resultados efetivamente observados nesta preparação:

- o Compose iniciou os containers existentes sem apagar volumes;
- PostgreSQL ficou `healthy`, a API respondeu HTTP 200 em
  `http://127.0.0.1:8081/health/ready` e o frontend respondeu HTTP 200 em
  `http://127.0.0.1:3000`;
- `npm ci` concluiu sem vulnerabilidades conhecidas pelo npm;
- a primeira execução frontend excedeu o limite de cinco segundos em um teste
  de indisponibilidade do login: 248 de 249 testes passaram;
- o arquivo afetado passou isoladamente com 8 de 8 testes e a repetição completa
  passou com 249 de 249 testes em 27 arquivos;
- lint e build do frontend passaram; o build manteve apenas o aviso conhecido de
  chunk JavaScript acima de 500 kB;
- como o SDK .NET não estava disponível no host, restore e build foram executados
  na imagem oficial .NET SDK 10 e passaram sem avisos ou erros;
- os testes de Domain e Application passaram, respectivamente, com 42 de 42 e
  40 de 40 casos;
- a suíte de integração não pôde ser validada nesse container auxiliar: 10 casos
  passaram e 84 não inicializaram o PostgreSQL descartável por uma limitação de
  conectividade entre o Testcontainers e o Docker Desktop. Esse resultado é uma
  limitação do ambiente de execução, não evidência de aprovação nem de regressão
  funcional.

Os números desta seção pertencem somente à execução de 12 de setembro. Eles não
substituem nem reescrevem os resultados históricos de 9 de setembro. Antes da
sessão institucional, a suíte de integração deve ser repetida em um host com o
SDK .NET 10 e acesso direto ao Docker, e os checks do SHA demonstrado devem ser
confirmados no GitHub.

### 3.9. Incrementos incorporados desde o ensaio histórico

| Capacidade atualizada                                      | Rastreabilidade      | Situação para a homologação                                          |
| ---------------------------------------------------------- | -------------------- | -------------------------------------------------------------------- |
| Associar opcionalmente uma entrada a evento                | Issue #176 / PR #183 | implementação técnica incorporada; compreensão e utilidade pendentes |
| Corrigir descrição com justificativa e auditoria           | Issue #175 / PR #184 | implementação técnica incorporada; política institucional pendente   |
| Registrar entrada com opções rápidas e campos condicionais | Issue #177 / PR #185 | hipóteses reversíveis do MVP pendentes de avaliação                  |
| Operar a lista compacta de acessos abertos                 | Issue #178 / PR #186 | volume real de pico e aceitação pendentes                            |
| Navegar por agrupamentos conforme o perfil                 | Issue #180 / PR #187 | nomenclatura e encontrabilidade pendentes                            |

A Issue #163 continua sendo uma pesquisa separada. Presença de servidores e
terceirizados não deve ser demonstrada como funcionalidade pronta deste MVP.

### 3.10. Retorno institucional de 21 de setembro de 2026

Uma apresentação ao contato principal do processo e à coordenação acadêmica
confirmou que a direção funcional do MVP veicular representa a rotina esperada e
pode seguir para a próxima etapa. Esse retorno é um aceite do recorte funcional,
não uma autorização de produção e nem substitui a sessão observada com pessoas
que exercerão os quatro perfis.

Foram confirmadas as seguintes decisões:

- cada operador terá conta individual;
- a direção futura é usar matrícula ou outro identificador institucional único
  no login, mantendo nome e perfil separados;
- matrícula não será usada como senha temporária;
- dois Administradores funcionais serão responsáveis por contas, recuperação de
  acesso e manutenção de autorizações de eventos;
- a credencial temporária será gerada aleatoriamente pelo servidor, entregue
  diretamente pelo Administrador e substituída obrigatoriamente no primeiro
  acesso;
- alunos e docentes responsáveis pelo projeto exercerão manutenção técnica e
  transferência de conhecimento, sem assumir a aprovação funcional diária;
- o Vigilante usará a própria conta ao substituir o Porteiro, inclusive nos
  intervalos, e a troca de operador exigirá logout;
- a sessão poderá ser renovada durante o uso, será bloqueada após 15 minutos sem
  atividade e terá duração absoluta limitada ao turno;
- o fechamento operacional ocorre às 22h; depois desse horário, vínculo com o
  campus isoladamente não autoriza entrada;
- a exceção posterior ao fechamento dependerá de motivo e autorização
  verificável da Administração, Direção, responsável pelo evento ou setor, ou
  lista previamente cadastrada;
- correções preservarão valor anterior, valor novo, motivo, horário e ator na
  auditoria;
- Administrador e Setor de Transporte poderão consultar a auditoria;
- documentos serão apenas conferidos presencialmente no recorte atual; fotos e
  cópias não serão armazenadas;
- as planilhas oficiais continuarão como contingência durante indisponibilidade;
  a reconciliação posterior deverá preservar horário observado, autoria do papel,
  responsável pela digitação e momento da transcrição;
- permanências curtas usarão previsão informada na entrada e alerta quando o
  tempo for ultrapassado, sem uma regra exclusiva para mototáxi;
- pedestres, bicicletas e os demais formulários físicos serão estudados como
  evolução posterior para evitar operação simultânea em papel e sistema.

Continuam pendentes antes de produção:

- observar Porteiro e Vigilante executando os fluxos e medir um volume de pico
  representativo;
- comprovar formato, unicidade, estabilidade e cobertura do identificador
  institucional para todos os vínculos, inclusive terceirizados;
- definir a política institucional para estudantes autorizados a conduzir ou
  manter veículo no campus, sem armazenar imagem de documento;
- identificar o enquadramento e a fonte normativa da retenção. A reunião relatou
  cinco anos, enquanto a tabela federal de atividades-meio de 2024 apresenta
  prazo de dois anos para controle de visitas e visitantes; o setor arquivístico
  ou a referência institucional de proteção de dados precisa decidir o caso
  aplicável;
- confirmar responsável pelo backup e restauração, ambiente definitivo, RPO,
  RTO, proteção de chaves e periodicidade dos exercícios;
- definir quem confere a transcrição da contingência e o destino do papel;
- definir conteúdo, destinatário, periodicidade e formato dos relatórios;
- transformar cada mudança aprovada em issue independente antes de alterar o
  comportamento implementado.

### 3.11. Decisões de continuidade registradas em 29 de setembro de 2026

O responsável pelo processo confirmou Eurico, do Setor de Transporte, como dono
funcional. A manutenção técnica será transferida entre as equipes designadas pelo
curso de Engenharia de Software, acompanhadas pelos orientadores.

Também foram registradas as seguintes decisões:

- cinco anos para registros operacionais, auditoria e formulários físicos de
  contingência, sujeitos à confirmação do enquadramento arquivístico antes de
  automatizar o descarte;
- formulários produzidos durante indisponibilidade serão digitados no sistema,
  conferidos e mantidos como evidência física pelo mesmo prazo;
- RPO-alvo de uma hora e RTO-alvo de quatro horas, com contingência imediata em
  papel;
- OCI Object Storage privado, OCI Vault, instance principal, MFA e separação de
  grupos IAM como desenho de produção;
- primeiro exercício de recuperação e contingência durante a segunda unidade.

O dump diário atual ainda comprova somente RPO técnico de até 24 horas. A Issue
#311 implementará a cópia protegida e a Issue #312 tratará WAL/PITR para comprovar
o RPO de uma hora. Nenhuma dessas decisões autoriza produção antes do exercício.

## 4. Ordem da demonstração

Antes de iniciar, substitua na cópia `*.local.http`:

- credenciais locais de demonstração;
- tokens retornados pelos quatro logins;
- IDs retornados pelas criações;
- data local da sessão;
- início e fim UTC do evento, envolvendo o horário real da demonstração.

Não versione essas substituições. Ao final, exclua a cópia local quando os
valores não forem mais necessários.

### Cenário 1 — disponibilidade e autenticação

| Campo    | Valor                                                               |
| -------- | ------------------------------------------------------------------- |
| Perfil   | Anônimo e Administrador                                             |
| Ação     | consultar readiness, autenticar e acessar endpoint protegido        |
| Esperado | readiness 200; login válido 200; requisição protegida sem token 401 |
| Pergunta | contas individuais e troca de operador representam o processo real? |

O Administrador cria contas fictícias para `Porteiro`, `Vigilante` e
`SetorTransporte`. Confirme que os usuários não compartilham credenciais.

### Cenário 2 — acesso geral e troca de turno

| Campo    | Valor                                                                                                                                                                                |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Perfil   | Porteiro; depois Vigilante                                                                                                                                                           |
| Ação     | com cada perfil, registrar entradas consecutivas, operar acessos abertos, consultar histórico, registrar saída e corrigir descrição com justificativa                                |
| Esperado | horário e ator definidos no servidor; placa normalizada; um único acesso aberto; saída preserva autoria; correção não altera placa, condutor, horários, situação ou autoria original |
| Pergunta | os campos e a lista de abertos são suficientes para a troca de turno?                                                                                                                |

O Vigilante usa a própria conta ao assumir a portaria e possui as mesmas
permissões operacionais do Porteiro. Não simule transferência de autoria do
registro original.

No formulário de entrada, valide placa e condutor como campos prioritários, as
categorias canônicas, os objetivos rápidos e a opção “Outro” com texto livre.
Percorra Atendimento em setor, Reunião, Entrega ou retirada de material,
Serviço ou manutenção, Participação em atividade e Embarque ou desembarque sem
tratá-los como regras institucionais já aprovadas.
Confirme as nove opções de tipo de veículo: Automóvel, Motocicleta, Caminhonete,
Van, Micro-ônibus, Ônibus, Caminhão, Veículo de emergência e Outro. Quando
“Outro” for escolhido, valide o campo de tipo personalizado. Expanda e recolha
os detalhes adicionais sem perder a observação e associe, de forma explícita e
opcional, uma autorização de evento. Nenhum evento pode ser escolhido
automaticamente nem apresentado como liberação do veículo pelo frontend.

Durante a sequência de pico, confirme que “Registrar e continuar” limpa somente
uma entrada concluída, anuncia o sucesso e devolve o foco à placa. Em seguida,
use “Registrar e ver acessos” para comprovar a navegação alternativa. Uma falha
de validação ou da API deve preservar os valores para correção e não pode manter
uma confirmação anterior. Registre tempo, ajuda, erros e interrupções como
evidência diagnóstica, sem impor uma meta arbitrária.

Na lista de acessos em aberto, confira linhas compactas no desktop e cartões
compactos no mobile. Use a busca local, diferencie o tempo transcorrido do acesso
do horário da última resposta bem-sucedida da API e execute uma atualização
manual. Uma falha de atualização deve preservar a lista anterior e avisar que os
dados podem estar desatualizados, sem criar contagem zero ou lista vazia falsa.
Ao registrar a saída, confira identificação por placa e condutor, confirmação,
controle e retorno de foco e exatamente uma mutação, mesmo diante de clique ou
submissão repetidos.

A correção auditada está disponível para Porteiro, Vigilante e Administrador; o
Setor de Transporte permanece somente leitura. Somente objetivo, categoria e
observação podem ser alterados, sempre com justificativa. Placa, condutor,
horários, situação e autoria original permanecem imutáveis.

O encerramento excepcional de uma saída esquecida também é restrito a Porteiro,
Vigilante e Administrador. Ele exige motivo e observação, mantém o horário de
saída vazio quando desconhecido e apresenta separadamente o momento em que a
regularização foi feita. Confirme na interface que a ação exige confirmação
acessível, não repete a requisição e permanece visualmente distinta da saída
normal. A integração dessa interface deve ser validada antes de considerar o
fluxo homologado.

### Cenário 3 — fronteira do Setor de Transporte

| Campo    | Valor                                                                            |
| -------- | -------------------------------------------------------------------------------- |
| Perfil   | SetorTransporte                                                                  |
| Ação     | consultar histórico e resumo; tentar registrar entrada geral                     |
| Esperado | consultas 200; tentativa de operação geral 403                                   |
| Pergunta | supervisão sem alteração do fluxo geral corresponde à responsabilidade do setor? |

Se o cliente disser que o setor também opera a portaria, registre a observação;
não altere a permissão durante a sessão.

Confirme também a arquitetura de informação esperada por perfil:

- Porteiro e Vigilante possuem navegação idêntica, organizada em `Visão geral`,
  `Operações` e `Consultas de apoio`;
- Setor de Transporte possui `Visão geral`, `Supervisão` e `Gestão`, sem entrada
  geral nem acessos gerais em aberto;
- Administrador possui `Visão geral`, `Supervisão`, `Consultas de apoio` e
  `Gestão técnica`; operações excepcionais continuam acessíveis por rota direta,
  mas não são promovidas no menu rotineiro.

Esses agrupamentos organizam a interface, mas não substituem a autorização do
backend e permanecem hipóteses reversíveis até a homologação.

### Cenário 4 — frota e motoristas autorizados

| Campo    | Valor                                                                                                                   |
| -------- | ----------------------------------------------------------------------------------------------------------------------- |
| Perfil   | SetorTransporte para cadastros; Porteiro para saída e retorno                                                           |
| Ação     | cadastrar veículo, autorizar motorista, registrar saída, listar uso aberto, registrar retorno e consultar histórico     |
| Esperado | apenas motorista explicitamente autorizado inicia viagem; retorno exige quilometragem válida; histórico fica preservado |
| Pergunta | quem deve registrar saída e retorno e quais dados realmente ajudam na conferência?                                      |

Documento do motorista é opcional no MVP. Não use essa demonstração para
decidir CNH, categoria, validade ou escala sem o responsável institucional.

### Cenário 5 — autorização de evento

| Campo    | Valor                                                                                                             |
| -------- | ----------------------------------------------------------------------------------------------------------------- |
| Perfil   | SetorTransporte para gestão; Porteiro para consulta e entrada                                                     |
| Ação     | criar evento vigente, consultar, registrar entrada vinculada e verificar consumo da regra                         |
| Esperado | evento ativo e vigente; placa específica tem precedência; cota não é excedida; saída não devolve a vaga consumida |
| Pergunta | o setor normalmente conhece placas, tipos/quantidades ou ambos?                                                   |

Pernoite informa uma autorização administrativa, mas não abre portão
automaticamente nem cria um bloqueio fixo às 23h.

### Cenário 6 — resumo operacional diário

| Campo    | Valor                                                                                                                              |
| -------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Perfil   | os quatro perfis                                                                                                                   |
| Ação     | consultar a data local da demonstração                                                                                             |
| Esperado | totais gerais, institucionais e de evento; `openAtStart` e `openAtEnd`; nenhuma pessoa, documento, placa, itinerário ou observação |
| Pergunta | os totais ajudam a troca de turno e a conferência? Os nomes dos campos são compreensíveis?                                         |

O resumo não classifica atraso ou irregularidade e não representa assinatura de
fechamento. Essas possibilidades só entram no backlog se forem solicitadas e
justificadas.

### Cenário 7 — contas e auditoria

| Campo    | Valor                                                                                                  |
| -------- | ------------------------------------------------------------------------------------------------------ |
| Perfil   | Administrador                                                                                          |
| Ação     | pesquisar contas e consultar auditoria por período, entidade e ator                                    |
| Esperado | somente Administrador; sem hash, senha, token ou dados pessoais duplicados; ações críticas rastreáveis |
| Pergunta | quem exercerá a administração e quem poderá revisar a trilha no ambiente real?                         |

Desativação e reativação podem ser demonstradas somente em conta fictícia que
não será usada nos demais cenários. O Administrador não pode desativar a própria
conta nem remover o último Administrador ativo.

Produção, retenção, backup e contingência não são aprovados por esta sessão. As
decisões de liberação e governança continuam nas Issues #30 e #100; este roteiro
apenas coleta evidências e dúvidas que possam apoiar essas decisões.

## 5. Matriz de rastreabilidade

| Capacidade do MVP               | Contratos principais                                                          | Evidência automatizada                                                                                    |
| ------------------------------- | ----------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Saúde técnica                   | `GET /health`, `/health/live`, `/health/ready`                                | `TechnicalEndpointsTests`                                                                                 |
| Login e bloqueio                | `POST /auth/login`                                                            | `AuthenticationTests`, `UsuarioTests`                                                                     |
| Administração de contas         | `GET/POST/DELETE /users`, reativação                                          | `UserAccountLifecycleTests`, `CreateUserAccountServiceTests`, `UserAccountLifecycleServiceTests`          |
| Entrada, abertos e saída geral  | `/access-records/entries`, `/open`, `/{id}/exit`, `/{id}/exceptional-closure` | `VehicleAccessTests`, `VehicleAccessServiceTests`, `RegistroAcessoTests`                                  |
| Histórico e correção geral      | `/access-records/history`, `/{id}/correction`                                 | `VehicleAccessTests`, `VehicleAccessServiceTests`                                                         |
| Catálogo de frota               | `/institutional-vehicles`                                                     | `InstitutionalVehicleCatalogTests`, `InstitutionalVehicleCatalogServiceTests`, `VeiculoTests`             |
| Motoristas autorizados          | `/institutional-drivers`                                                      | `InstitutionalDriverTests`, `InstitutionalDriverServiceTests`, `MotoristaInstitucionalTests`              |
| Uso institucional               | `/institutional-vehicle-usages`                                               | `InstitutionalVehicleUsageTests`, `InstitutionalVehicleUsageServiceTests`, `UsoVeiculoInstitucionalTests` |
| Autorizações de eventos         | `/event-authorizations`                                                       | `EventAuthorizationTests`, `EventAuthorizationServiceTests`, `EventoAcessoTests`                          |
| Entrada vinculada ao evento     | `POST /access-records/entries` com `eventAuthorizationId`                     | `EventAccessAssociationTests`                                                                             |
| Formulário de entrada otimizado | campos existentes de `POST /access-records/entries`                           | testes de `NewAccessPage` e validação dos campos condicionais                                             |
| Lista compacta de abertos       | `GET /access-records/open`, `POST /{id}/exit`                                 | testes de `OpenAccessPage`, `OpenAccessList` e cálculo de tempo                                           |
| Navegação por perfil            | rotas e políticas existentes                                                  | testes de `routeMetadata`, `ProfileRoute` e `AppLayout`                                                   |
| Resumo diário                   | `GET /operations/daily-summary`                                               | `OperationalSummaryTests`, `OperationalSummaryServiceTests`                                               |
| Auditoria administrativa        | `GET /audits`                                                                 | `AuditTrailTests`, `AuditTrailServiceTests`, `AuditoriaTests`                                             |
| Segurança das requisições       | middleware, Problem Details e rate limiting                                   | `RequestSafetyTests`, `RateLimitingTests`                                                                 |
| PostgreSQL e migrations         | schema `dbo`, constraints e índices                                           | `PostgreSqlPersistenceTests`, setup de `ApiFactory`                                                       |
| Fronteiras arquiteturais        | dependências entre projetos                                                   | `ArchitectureTests`                                                                                       |

A suíte automatizada reduz regressões técnicas, mas não substitui a avaliação de
clareza, utilidade e adequação do processo pelos usuários.

### Continuidade operacional

- [ ] Simular indisponibilidade e ativar o
      [formulário de contingência](../operations/contingency-record-template.md) numerado.
- [ ] Registrar no papel uma entrada, uma saída e um veículo ainda presente.
- [ ] Confirmar os campos mínimos e se documento pessoal pode continuar opcional.
- [ ] Confirmar quem aciona TI e Setor de Transporte e por qual canal.
- [ ] Validar dupla conferência e fechamento da reconciliação pelo setor responsável.
- [x] Registrar RPO-alvo de 1 hora e RTO-alvo de 4 horas.
- [x] Definir Eurico como responsável funcional e retenção operacional de cinco
      anos para o formulário reconciliado.
- [ ] Designar substituto do responsável, membros dos grupos IAM e referência
      arquivística ou de proteção de dados.
- [ ] Executar o exercício na OCI durante a segunda unidade e medir RPO/RTO.
- [ ] Não usar os endpoints atuais para simular horários históricos; registrar a
      necessidade do fluxo específico de reconciliação.

O roteiro completo e o registro das decisões ficam no
[plano de retenção, backup e continuidade](../operations/data-retention-and-continuity.md).

## 6. Checklist por perfil

### Porteiro

- [ ] Consegue autenticar com conta individual.
- [ ] Entende os campos obrigatórios da entrada.
- [ ] Usa os dois caminhos de conclusão e consegue iniciar entradas consecutivas.
- [ ] Localiza registros abertos sem consultar papel.
- [ ] Registra saída sem reescrever o registro.
- [ ] Corrige somente dados descritivos, com justificativa e auditoria.
- [ ] Consulta frota, motoristas e eventos sem gerenciá-los.
- [ ] Entende o resumo diário.
- [ ] Confirma que o fluxo é viável em horário de pico.

### Vigilante

- [ ] Assume o fluxo com a própria conta.
- [ ] Enxerga os registros deixados pelo turno anterior.
- [ ] Possui as mesmas permissões operacionais do Porteiro.
- [ ] Corrige somente dados descritivos, com justificativa e auditoria.
- [ ] Não consegue gerenciar contas, frota, motoristas ou eventos.
- [ ] Confirma as exceções após o fechamento operacional da portaria.

### Setor de Transporte

- [ ] Consulta históricos geral e institucional.
- [ ] Mantém frota e autorizações de motoristas.
- [ ] Mantém autorizações de eventos.
- [ ] Não altera registros gerais da portaria.
- [ ] Avalia se o resumo reduz a conferência manual.
- [ ] Define quais decisões precisam de relatório ou exportação futura.

### Administrador

- [ ] Cria e pesquisa contas sem visualizar hashes.
- [ ] Desativa e reativa conta fictícia com preservação do histórico.
- [ ] Consulta auditoria com filtros.
- [ ] Confirma quem terá esse papel no ambiente institucional.
- [ ] Reconhece que recuperação de senha e produção ainda possuem pendências.

## 7. Registro do feedback

Registre primeiro o contexto da sessão, sem nomes pessoais quando o perfil for
suficiente:

| Campo                    | Valor observado                                 |
| ------------------------ | ----------------------------------------------- |
| Data e horário           | AAAA-MM-DD HH:mm                                |
| Commit demonstrado       | SHA completo de `main`                          |
| Ambiente                 | Local ou homologação isolada                    |
| Navegador e sistema      | Nome e versão observados                        |
| Larguras verificadas     | Aproximadamente 390 px e 1440 px                |
| Perfis representados     | Porteiro, Vigilante, Transporte e Administrador |
| Volume de pico combinado | Quantidade definida com Portaria/Vigilância     |
| Uso somente por teclado  | Sim/Não; cenário executado                      |

Copie uma linha por tarefa durante ou imediatamente após a sessão. Meça o tempo
como diagnóstico e não como meta arbitrária de aprovação:

| Perfil   | Cenário e tarefa           | Conclusão | Ajuda               | Tempo observado | Erros ou retornos  | Consulta ao papel/repetição | Interrupção e retomada | Resultado                         | Prioridade       | Issue |
| -------- | -------------------------- | --------- | ------------------- | --------------- | ------------------ | --------------------------- | ---------------------- | --------------------------------- | ---------------- | ----- |
| Porteiro | Registrar entrada fictícia | Sim/Não   | Sem ajuda/Com ajuda | mm:ss           | descrição objetiva | Sim/Não; o quê              | descrição objetiva     | Aceito/Ajustar/Rejeitado/Pendente | Alta/Média/Baixa | `#NN` |

Uma observação pode explicar o problema, mas não substitui a decisão. Registre
separadamente o que a pessoa fez, o que disse e o que foi decidido para evitar
transformar interpretação da equipe em requisito institucional.

Perguntas que precisam de resposta explícita:

1. Os objetivos rápidos representam os casos frequentes?
2. Está faltando algum tipo de veículo?
3. “Operações”, “Consultas de apoio”, “Supervisão”, “Gestão” e “Gestão técnica”
   são compreensíveis?
4. Cada perfil encontra suas tarefas sem ajuda?
5. A lista compacta funciona com o volume real de pico combinado com a Portaria
   e a Vigilância?
6. O horário de atualização e o tempo transcorrido são compreendidos sem parecer
   alerta?
7. Quem registra saídas e retornos dos veículos institucionais na prática?
8. O resumo diário atende à troca de turno e à conferência do Transporte?
9. Quais exceções após o fechamento operacional precisam apenas de registro?
10. Para eventos, são informadas placas, tipos e quantidades ou uma combinação?
11. Quais correções podem ser feitas e por qual perfil?
12. Há necessidade comprovada de exportação? Para quem e com quais dados?
13. Quem será Administrador e quem poderá consultar auditoria?
14. Quais decisões das Issues #30 e #100 possuem responsáveis e prazo?

## 8. Critério de encerramento

A homologação técnica está pronta para ser registrada quando:

- [ ] ambiente e readiness foram verificados;
- [ ] os sete cenários foram executados ou justificados como não aplicáveis;
- [ ] cada perfil relevante participou ou teve representante identificado;
- [ ] cada resultado foi classificado;
- [ ] dúvidas sem resposta foram mantidas como pendentes;
- [ ] mudanças aprovadas receberam issues independentes;
- [ ] nenhuma credencial ou dado pessoal foi incluído nas evidências;
- [ ] a data, os participantes e a versão/commit demonstrado foram registrados;
- [ ] o sistema permaneceu classificado como não produtivo até concluir as
      Issues #30 e #100.

Depois da reunião, publique na Wiki apenas decisões consolidadas. Anotações
brutas, credenciais, tokens, documentos e dados pessoais não devem ser
versionados.
