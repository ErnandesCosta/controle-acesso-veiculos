# Tokens semânticos do frontend

## Objetivo

Esta fundação separa a identidade visual do IFPE da finalidade de cada cor na
interface. Componentes devem consumir tokens semânticos; valores da paleta-base
ficam restritos à definição do tema e a usos institucionais explícitos.

A introdução destes tokens não ativa tema escuro nem redesenha páginas. O tema
claro continua sendo o padrão e os aliases antigos preservam o visual atual até
a migração incremental das Issues #403–#406. A ativação de Claro, Escuro e
Sistema pertence à Issue #407.

## Camadas

1. **Paleta-base:** valores institucionais e neutros, como os verdes do IFPE,
   creme e branco. Não devem ser usados diretamente por componentes comuns.
2. **Contrato semântico:** variáveis `--ui-*` em
   `src/frontend/src/styles/design-tokens.css`, redefinidas por tema.
3. **Utilitários de componente:** nomes Tailwind `bg-background`, `bg-surface`,
   `text-text`, `text-text-muted`, `border-border`, `bg-primary` e famílias de
   estado. A nomenclatura descreve função, não tonalidade.

Os aliases `ink`, `ink-soft`, `brand-dark`, `brand-soft` e `cream` são somente
uma ponte de compatibilidade. Eles devem desaparecer à medida que os componentes
forem migrados, sem uma substituição mecânica que crie combinações inválidas.

## Contrato mínimo

| Grupo          | Tokens                                                      |
| -------------- | ----------------------------------------------------------- |
| Estrutura      | `background`, `surface`, `surface-raised`, `surface-subtle` |
| Texto          | `text`, `text-muted`, `text-inverse`                        |
| Limites        | `border`, `border-strong`, `focus`                          |
| Ação principal | `primary`, `primary-hover`, `primary-text`                  |
| Profundidade   | `overlay`, `shadow`                                         |
| Estados        | `success-*`, `warning-*`, `danger-*`, `disabled-*`          |

Cada família de estado possui superfície, texto e borda. O significado também
deve aparecer em texto ou nome acessível; cor sozinha não comunica estado.

## Matriz de contraste

Os valores abaixo foram calculados pela luminância relativa definida na WCAG
2.2. O projeto adota pelo menos 4,5:1 para texto normal e 3:1 para foco, bordas
essenciais e demais informações visuais necessárias. Referências:
[contraste mínimo](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html)
e [contraste não textual](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html).

### Claro

| Combinação prevista                    |   Razão |
| -------------------------------------- | ------: |
| `text` / `background`                  |  9,67:1 |
| `text` / `surface`                     | 10,11:1 |
| `text-muted` / `background`            |  6,90:1 |
| `text-muted` / `surface`               |  7,21:1 |
| `primary-text` / `primary`             |  6,38:1 |
| `primary-text` / `primary-hover`       |  8,55:1 |
| `border` / `background`                |  4,11:1 |
| `border` / `surface`                   |  4,29:1 |
| `border-strong` / `background`         |  6,90:1 |
| `focus` / `background`                 |  6,97:1 |
| `focus` / `surface`                    |  7,28:1 |
| `success-text` / `success-surface`     |  7,27:1 |
| `success-border` / `success-surface`   |  3,81:1 |
| `warning-text` / `warning-surface`     |  7,23:1 |
| `warning-border` / `warning-surface`   |  4,16:1 |
| `danger-text` / `danger-surface`       |  8,18:1 |
| `danger-border` / `danger-surface`     |  4,53:1 |
| `disabled-text` / `disabled-surface`   |  5,28:1 |
| `disabled-border` / `disabled-surface` |  3,22:1 |

### Escuro preparado, ainda não ativado

| Combinação prevista                    |   Razão |
| -------------------------------------- | ------: |
| `text` / `background`                  | 16,22:1 |
| `text` / `surface`                     | 14,25:1 |
| `text-muted` / `background`            | 10,88:1 |
| `text-muted` / `surface`               |  9,56:1 |
| `primary-text` / `primary`             |  8,09:1 |
| `primary-text` / `primary-hover`       |  9,38:1 |
| `border` / `background`                |  5,19:1 |
| `border` / `surface`                   |  4,57:1 |
| `border-strong` / `background`         | 10,25:1 |
| `focus` / `background`                 | 12,15:1 |
| `focus` / `surface`                    | 10,68:1 |
| `success-text` / `success-surface`     |  9,23:1 |
| `success-border` / `success-surface`   |  4,56:1 |
| `warning-text` / `warning-surface`     | 10,37:1 |
| `warning-border` / `warning-surface`   |  5,71:1 |
| `danger-text` / `danger-surface`       |  9,88:1 |
| `danger-border` / `danger-surface`     |  4,42:1 |
| `disabled-text` / `disabled-surface`   |  6,12:1 |
| `disabled-border` / `disabled-surface` |  3,19:1 |

Essas razões validam somente as combinações indicadas. Opacidade, mistura de
cores, imagens ou sobreposição podem alterar o resultado e exigem nova medição.
Em especial, `text-muted` não deve ser colocado diretamente sobre o antigo
verde suave sem comprovação específica.

## Inventário de migração

Na criação desta fundação, o código de produção ainda continha:

- 22 linhas com hexadecimal direto em 9 arquivos, fora dos arquivos de tokens;
- 90 ocorrências de `bg-white`;
- 80 ocorrências de `bg-cream`;
- 168 ocorrências de famílias diretas `red`, `amber`, `emerald` ou `slate`.

Esse inventário é uma linha de base, não uma autorização para substituição em
massa. A migração deve ocorrer por componente e preservar hierarquia, contraste,
estados, foco, responsividade e regras de negócio.

## Restrições de segurança e implementação

- tokens e preferência visual não carregam identidade ou autorização;
- JWT, senha, credencial, e-mail, perfil e dados pessoais não pertencem ao tema;
- nenhuma preferência é persistida nesta etapa;
- o atributo `data-theme="dark"` está reservado ao controlador futuro e não é
  aplicado pelo código de produção atual;
- a autorização da API e as capacidades de rota não podem depender de aparência;
- nova combinação deve ser medida antes de entrar no contrato recomendado.
