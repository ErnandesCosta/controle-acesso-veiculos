# ADR 0001 — Ciclo de vida seguro de sessões

- **Estado:** aceita; política temporal validada institucionalmente
- **Data:** 24 de setembro de 2026
- **Rastreabilidade:** Issues #190, #191 e #268

## Contexto

O JWT de acesso expira em 15 minutos e fica somente em memória no frontend. Isso
limita o impacto de uma exposição, mas exige novo login ao expirar ou recarregar
a página. A estação da portaria opera durante turnos longos. Aumentar apenas a
validade do JWT ampliaria a janela de abuso e não permitiria revogação confiável.

O sistema é first-party e usa frontend e API no mesmo site por meio do proxy
`/api`. Aplicativos não web futuros poderão continuar usando o contrato de access
token apropriado ao seu canal, sem receber automaticamente o cookie do navegador.

## Decisão

Adotar access token curto em memória e uma família de refresh tokens opacos:

- 256 bits de aleatoriedade obtidos do CSPRNG da plataforma;
- valor bruto somente em cookie `HttpOnly`, `SameSite=Strict`, `Secure` em
  produção e caminho mínimo `/auth`;
- somente SHA-256 do valor bruto no PostgreSQL;
- rotação a cada renovação, sob transação e bloqueio de linha;
- reutilização de token consumido revoga a família conhecida e gera auditoria;
- logout e desativação de conta revogam a família no servidor;
- perfil ou conta inativo não renova;
- 15 minutos sem atividade humana e 12 horas de duração absoluta, conforme
  decisão institucional; os valores permanecem configuráveis por ambiente e o
  JWT continua com 15 minutos;
- renovação e logout exigem token antifalsificação emitido pelo servidor;
- nenhuma credencial de sessão é aceita em URL, armazenamento web, log ou
  auditoria.

O Nginx same-origin traduz o caminho de cookie `/auth` para o caminho público
`/api/auth`. Qualquer proxy alternativo precisa preservar HTTPS, cabeçalhos e
essa correspondência de caminho.

O ambiente `LocalContainer` permite cookies sem `Secure` apenas para o Compose
HTTP em loopback. Produção exige HTTPS e um key ring do ASP.NET Core Data
Protection persistente, criptografado e compartilhado entre réplicas. A stack
local usa chaves efêmeras e, após reinício, o cliente obtém um novo par CSRF.

## Contrato para o frontend

1. Ao iniciar ou recarregar, obter `requestToken` em `GET /auth/csrf`.
2. Enviar esse valor no cabeçalho `X-CSRF-TOKEN` de `POST /auth/refresh` e
   `POST /auth/logout`. O navegador transporta os cookies; o JavaScript não lê o
   refresh token.
3. Manter o JWT retornado por login/refresh somente em memória.
4. Antes da expiração, renovar uma única vez; após um 401 elegível, permitir no
   máximo uma renovação e uma repetição da requisição original.
5. Coordenar abas para que apenas uma delas renove por vez. Renovação paralela do
   mesmo token é tratada como reutilização e falha de forma segura.
6. Se a renovação falhar, limpar a identidade em memória e solicitar login, sem
   loop e sem revelar o motivo interno.
7. No logout, tentar a revogação; independentemente da resposta, apagar o estado
   local. Indisponibilidade deve ser apresentada sem afirmar revogação concluída.
8. Ler os cabeçalhos `X-Session-Server-Time`,
   `X-Session-Inactivity-Expires-At` e `X-Session-Absolute-Expires-At` emitidos no
   login e na renovação. Eles permitem calcular durações usando uma única
   referência do servidor e não expõem credenciais.
9. Renovar somente quando houve atividade humana relevante dentro da janela. Um
   temporizador em segundo plano, mudança de aba ou retomada do dispositivo não
   contam como atividade. Ao atingir qualquer prazo, encerrar o estado local e
   coordenar o encerramento entre abas.

## Alternativas consideradas

- **JWT longo:** rejeitado por ampliar a janela de comprometimento e não oferecer
  revogação de sessão.
- **Refresh token em `localStorage` ou `sessionStorage`:** rejeitado por expor a
  credencial a JavaScript e persistir seu impacto em caso de XSS.
- **Refresh token estático:** rejeitado por impedir detecção de reutilização.
- **Cookie de autenticação para todas as chamadas/BFF:** reduz ainda mais a
  exposição de tokens no navegador, mas exigiria trocar o contrato já integrado,
  proteger todas as mutações contra CSRF e separar clientes não web. Pode ser
  reavaliado antes de uma arquitetura distribuída ou SSO institucional.
- **OAuth/OIDC institucional:** desejável no futuro, mas depende de provedor,
  governança e disponibilidade ainda não confirmados.

## Consequências

- a sessão sobrevive a recarregamentos sem persistir JWT no navegador;
- roubo do access token continua limitado à sua validade curta;
- replay, logout e desativação passam a ter estado revogável no servidor;
- cada renovação grava uma nova linha e exige política posterior de retenção e
  limpeza alinhada à Issue #30;
- HTTPS deixa de ser apenas recomendação: é requisito para cookies de produção;
- múltiplas abas e respostas perdidas recebem tratamento conservador; a política
  de ociosidade compartilhada e retomada do dispositivo pertence à Issue #268;
- a proteção completa do tablet depende da integração do cliente e de validação
  humana no dispositivo usado pela portaria.

## Referências

- [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [ASP.NET Core antiforgery](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [OAuth 2.0 for Browser-Based Applications](https://datatracker.ietf.org/doc/draft-ietf-oauth-browser-based-apps/27/)
