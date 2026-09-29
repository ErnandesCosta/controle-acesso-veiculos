# Backup e restauração local do PostgreSQL

Este diretório contém o procedimento técnico da Issue #67 para gerar um backup
lógico e comprovar sua restauração em um banco isolado. Ele apoia a Issue #30,
mas não define retenção, RPO, RTO, criptografia ou contingência institucional.

## Pré-requisitos

- Docker Engine ou Docker Desktop em execução;
- plugin Docker Compose;
- arquivo `infrastructure/docker/.env` configurado localmente;
- serviço `postgresql` saudável e migrations aplicadas;
- PowerShell 5.1 ou superior.

Os scripts usam as variáveis já presentes no container PostgreSQL. A senha não é
incluída na linha de comando nem no arquivo de backup.

## Criar um backup

A partir da raiz do repositório:

```powershell
./infrastructure/database/Backup-PostgreSql.ps1
```

O dump é criado em `infrastructure/database/backups/`, no formato custom do
PostgreSQL, com nome UTC. Ao lado dele, o script cria o manifesto
`<arquivo>.dump.manifest.json`, contendo somente versão do formato, algoritmo,
nome, tamanho, SHA-256 e horário UTC. O diretório inteiro é ignorado pelo Git. O
script também valida a estrutura do arquivo com `pg_restore --list`.

## Comprovar a restauração

Informe o dump criado:

```powershell
./infrastructure/database/Test-PostgreSqlRestore.ps1 `
  -BackupPath ./infrastructure/database/backups/controle-acesso-AAAAmmddTHHMMSSfffZ.dump
```

O script:

1. exige e valida o manifesto correspondente;
2. confere nome, tamanho e SHA-256 antes de copiar o dump ao container;
3. valida a estrutura do arquivo;
4. cria um banco temporário com nome aleatório;
5. restaura todo o dump com falha explícita;
6. confirma a presença das tabelas essenciais;
7. remove o banco temporário e a cópia no container, inclusive após falha.

Ele não aceita um banco de destino e nunca substitui o banco operacional.

## Limites de segurança

O dump pode conter dados pessoais e históricos. Estar fora do Git não equivale a
estar protegido para produção. Não envie o arquivo por e-mail, mensageria ou
armazenamento pessoal. Em homologação ou produção, a instituição ainda deve
definir na Issue #30:

- responsável, frequência, retenção e descarte;
- armazenamento externo e criptografado, controle de acesso e rotação de chaves;
- RPO, RTO, monitoramento e evidências periódicas de restauração;
- contingência da portaria e reconciliação posterior;
- procedimento aprovado para restauração real, que é uma ação destrutiva.

O SHA-256 detecta corrupção ou troca acidental quando o manifesto permanece
íntegro. Ele não autentica a origem: quem puder substituir os dois arquivos
também poderá calcular outro hash. Portanto, o manifesto não substitui
criptografia, assinatura, armazenamento imutável ou controle de acesso.

Os scripts atuais são destinados exclusivamente ao desenvolvimento local e ao
ensaio técnico. Um backup só deve ser considerado recuperável após a restauração
ter sido testada com sucesso.

O workflow `CI - Database recovery` repete o ciclo com dados fictícios sempre que
os scripts ou a configuração local do PostgreSQL mudam. Ele comprova a
restauração do arquivo íntegro e a rejeição de uma cópia alterada. Essa
verificação evita regressões técnicas, mas não substitui ensaios periódicos com a
infraestrutura e os responsáveis institucionais reais.
