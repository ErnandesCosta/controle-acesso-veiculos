# ADR 0002 — Key ring persistente e protegido do ASP.NET Core

**Status:** Aceita para o MVP técnico; integração com OCI Vault/FSS pendente  
**Data:** 30 de setembro de 2026  
**Issue:** #355

## Contexto

O ASP.NET Core Data Protection protege, entre outros valores, o par antifalsificação
usado pelos endpoints de renovação e logout. O repositório efêmero padrão de um
container perde suas chaves quando a instância é substituída. Isso invalida valores
protegidos ainda legítimos e impede que duas réplicas validem o mesmo material.

Persistir XML sem proteção resolveria disponibilidade, mas transformaria acesso ao
volume em acesso direto às chaves mestras. Armazenar o certificado ou sua senha no
Git apenas deslocaria o segredo para outro artefato exposto.

## Decisão

- todas as réplicas usam `ApplicationName=controle-acesso-veiculos`;
- o key ring fica em diretório durável compartilhado e gravável somente pelo usuário
  da API;
- cada XML do key ring é protegido por certificado X.509 com chave privada;
- o PFX é montado como Docker secret somente para leitura;
- a senha do PFX é montada como um segundo secret, materializado por secret manager,
  e nunca integra variável do container ou o repositório;
- `Production` e `LocalContainer` falham ao iniciar quando a configuração segura não
  está habilitada;
- testes automatizados usam certificado e diretório descartáveis;
- produção usa volume externo, permitindo mapear o nome para armazenamento durável
  compartilhado, inclusive OCI File Storage quando a topologia for provisionada.

O key ring não substitui JWT, hash de refresh token ou controle de sessão no banco.
Ele protege o material criptográfico interno utilizado pelo framework.

## Rotação e revogação

O ASP.NET Core continua gerando e ativando chaves de Data Protection segundo seu
ciclo normal. A rotação do certificado de proteção deve:

1. preservar o certificado anterior enquanto existirem chaves protegidas por ele;
2. disponibilizar o novo certificado a todas as réplicas antes da troca;
3. reconfigurar a proteção e promover as réplicas de forma controlada;
4. validar tokens antifalsificação emitidos antes da substituição;
5. retirar o certificado anterior somente depois da janela de compatibilidade e de
   um backup verificado.

Comprometimento do certificado exige revogação operacional, substituição do PFX e do
key ring e encerramento conservador das sessões. Não se apagam chaves isoladas para
“revogar uma sessão”; sessões são revogadas no PostgreSQL pelos mecanismos próprios.

## Backup e restauração

O backup do key ring e o backup do certificado são separados. O key ring acompanha o
ponto de recuperação da aplicação; o PFX e sua senha permanecem no cofre de segredos.
Uma restauração é válida somente quando o diretório, o certificado correspondente e
o mesmo `ApplicationName` estão disponíveis. Logs, manifestos e tickets nunca devem
conter XML, PFX, senha ou chave privada.

## Consequências

Reiniciar ou substituir uma instância deixa de invalidar dados protegidos apenas por
perda de chave. Réplicas autorizadas conseguem compartilhar o key ring. Em troca, a
implantação passa a depender explicitamente do volume, do certificado e do mecanismo
de segredos; indisponibilidade ou rotação incorreta desses componentes deve falhar de
forma visível, sem recorrer silenciosamente a chaves efêmeras.
