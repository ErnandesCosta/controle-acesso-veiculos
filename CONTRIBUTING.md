# Como contribuir

Obrigado pelo interesse no Controle de Acesso de Veículos. Este projeto é um MVP
acadêmico aplicado ao IFPE Campus Belo Jardim. Mudanças devem preservar a
rastreabilidade, a segurança e a distinção entre funcionalidades implementadas,
hipóteses do MVP e decisões ainda pendentes de validação institucional.

## Antes de começar

1. Leia o [README](README.md) e as instruções do [AGENTS.md](AGENTS.md).
2. Consulte as regras de
   [repositório](.github/instructions/repository.instructions.md),
   [segurança](.github/instructions/security.instructions.md) e da área afetada.
3. Verifique issues, Pull Requests, Wiki e código atual antes de propor uma
   mudança.
4. Para comportamento, arquitetura ou fluxo, consulte também a documentação em
   [`docs/`](docs/) e a
   [Wiki](https://github.com/ifpebj-ti/controle-acesso-veiculos/wiki).

Documentos históricos explicam a evolução do projeto, mas não substituem a
implementação presente na `main` e as evidências atuais da CI.

## Responsabilidades por área

- Raíssa: frontend, UX, IHC e acessibilidade.
- José Ernandes: backend e banco de dados.
- Infraestrutura, DevOps, segurança operacional e QA: responsabilidade
  compartilhada.
- Eurico, do Setor de Transporte: principal contato para regras do processo.

Uma mudança de frontend não deve alterar silenciosamente contrato de API,
autorização ou regra de negócio. Da mesma forma, uma mudança de backend que exija
nova interação deve registrar a dependência do frontend.

## Fluxo de trabalho

Toda implementação começa em uma issue escrita em português, com contexto,
objetivo, escopo, critérios de aceite, dependências, riscos e itens fora de
escopo.

1. Atualize a `main` com `git pull --ff-only`.
2. Crie uma branch por issue:
   - `feature/issue-N-descricao`;
   - `fix/issue-N-descricao`;
   - `docs/issue-N-descricao`;
   - `chore/issue-N-descricao`.
3. Faça commits pequenos em inglês, seguindo
   [Conventional Commits](docs/development/commit-conventions.md).
4. Inclua `Refs #N` no corpo dos commits.
5. Abra o Pull Request com título e descrição em inglês.
6. Use `Refs #N` e mantenha o PR como draft enquanto houver critério pendente.
7. Use `Closes #N` somente quando todo o escopo estiver concluído e validado.

Não faça force push, rebase de trabalho compartilhado ou merge sem coordenação.
Depois do merge, alterações necessárias na Wiki devem ocorrer em tarefa separada
baseada na `main` atualizada.

## Ambiente e validações

Use os guias canônicos:

- [execução local e containers](README.md#início-rápido-com-docker);
- [frontend](src/frontend/README.md);
- [banco e recuperação](infrastructure/database/README.md);
- [CI/CD](docs/development/ci-cd.md);
- [implantação versionada](docs/operations/versioned-container-deployment.md).

Execute somente as validações aplicáveis à mudança, sem reduzir testes para obter
aprovação:

```bash
dotnet restore src/backend/ControleAcessoVeiculos.slnx
dotnet format src/backend/ControleAcessoVeiculos.slnx --no-restore --verify-no-changes
dotnet build src/backend/ControleAcessoVeiculos.slnx --no-restore
dotnet test src/backend/ControleAcessoVeiculos.slnx --no-build --no-restore

npm --prefix src/frontend ci
npm --prefix src/frontend test -- --run
npm --prefix src/frontend run lint
npm --prefix src/frontend run build

git diff --check
```

Testes de integração do backend exigem Docker disponível. Se uma validação não
puder ser executada, registre o motivo e não declare aprovação inexistente.

## Segurança, privacidade e dados

- Nunca versione `.env`, credenciais, tokens, chaves, cookies ou connection
  strings reais.
- Não use nomes, documentos, placas, e-mails ou planilhas reais em exemplos e
  testes.
- Dados de demonstração devem ser claramente fictícios.
- Autorização deve ser aplicada no backend; esconder controles no frontend não é
  uma barreira de segurança.
- Mudanças de atores, dados, fronteiras de confiança ou controles devem atualizar
  a [modelagem de ameaças](docs/security/threat-model.md).
- Vulnerabilidades devem seguir o [guia de segurança](docs/security/README.md),
  nunca uma issue pública com detalhes exploráveis.

## Revisão e entrega

Antes de solicitar revisão:

- revise o diff completo e faça stage direcionado;
- confirme que não existem arquivos estranhos ou mudanças fora do escopo;
- registre comandos e resultados realmente observados;
- diferencie comportamento confirmado, hipótese, pendência e fora de escopo;
- documente migração, compatibilidade, rollback e risco operacional quando
  aplicável;
- não apresente mock, protótipo ou dados fictícios como produção.

Pull Requests devem permanecer pequenos o suficiente para que outra pessoa possa
entender o motivo, o risco e a validação sem reconstruir o contexto inteiro do
projeto.
