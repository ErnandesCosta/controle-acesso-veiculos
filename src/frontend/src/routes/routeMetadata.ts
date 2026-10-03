import type { IconName } from "../components/ui/Icon";
import type { ProfileName } from "../features/authentication";

const applicationTitle = "Controle de Acesso de Veículos";

export const allProfiles = [
  "Porteiro",
  "Vigilante",
  "SetorTransporte",
  "Administrador",
] as const satisfies readonly ProfileName[];

export type ProtectedRoutePath =
  | "/visao-geral"
  | "/conta/senha"
  | "/acessos/novo"
  | "/acessos/abertos"
  | "/acessos/historico"
  | "/utilizacoes-institucionais"
  | "/frota"
  | "/motoristas-institucionais"
  | "/eventos"
  | "/administracao";

type NavigationSectionId =
  | "overview"
  | "operations"
  | "support"
  | "supervision"
  | "management"
  | "technical";

interface NavigationPlacement {
  profiles: readonly ProfileName[];
  section: NavigationSectionId;
}

interface RouteMetadata {
  allowedProfiles?: readonly ProfileName[];
  focusSelector?: string;
  navigation?: {
    icon: IconName;
    label: string;
    placements: readonly NavigationPlacement[];
  };
  title: string;
}

export interface ProfileNavigationItem {
  icon: IconName;
  label: string;
  to: ProtectedRoutePath;
}

export interface ProfileNavigationSection {
  icon: IconName;
  id: NavigationSectionId;
  items?: readonly ProfileNavigationItem[];
  label: string;
  to?: ProtectedRoutePath;
}

const portariaProfiles = ["Porteiro", "Vigilante"] as const;
export const operationalProfiles = [
  ...portariaProfiles,
  "Administrador",
] as const satisfies readonly ProfileName[];
const managementProfiles = [
  "SetorTransporte",
  "Administrador",
] as const satisfies readonly ProfileName[];

const sectionMetadata: Readonly<
  Record<NavigationSectionId, { icon: IconName; label: string }>
> = {
  management: { icon: "clipboard", label: "Gestão" },
  operations: { icon: "history", label: "Operações" },
  overview: { icon: "dashboard", label: "Visão geral" },
  supervision: { icon: "history", label: "Supervisão" },
  support: { icon: "clipboard", label: "Consultas de apoio" },
  technical: { icon: "shield", label: "Gestão técnica" },
};

const profileSectionOrder: Readonly<
  Record<ProfileName, readonly NavigationSectionId[]>
> = {
  Administrador: ["overview", "supervision", "support", "technical"],
  Porteiro: ["overview", "operations", "support"],
  SetorTransporte: ["overview", "supervision", "management"],
  Vigilante: ["overview", "operations", "support"],
};

export const routeMetadata: Readonly<Record<string, RouteMetadata>> = {
  "/login": { focusSelector: "#email", title: "Entrar" },
  "/conta/senha": {
    allowedProfiles: allProfiles,
    title: "Alterar senha",
  },
  "/visao-geral": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "dashboard",
      label: "Visão geral",
      placements: [{ profiles: allProfiles, section: "overview" }],
    },
    title: "Visão geral",
  },
  "/acessos/novo": {
    allowedProfiles: operationalProfiles,
    navigation: {
      icon: "plus",
      label: "Registrar entrada",
      placements: [{ profiles: portariaProfiles, section: "operations" }],
    },
    title: "Registrar entrada",
  },
  "/acessos/abertos": {
    allowedProfiles: operationalProfiles,
    navigation: {
      icon: "clock",
      label: "Acessos em aberto",
      placements: [{ profiles: portariaProfiles, section: "operations" }],
    },
    title: "Acessos em aberto",
  },
  "/acessos/historico": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "history",
      label: "Histórico de acessos",
      placements: [
        { profiles: portariaProfiles, section: "operations" },
        { profiles: managementProfiles, section: "supervision" },
      ],
    },
    title: "Histórico de acessos",
  },
  "/utilizacoes-institucionais": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "bus",
      label: "Utilizações da frota",
      placements: [
        { profiles: portariaProfiles, section: "operations" },
        { profiles: managementProfiles, section: "supervision" },
      ],
    },
    title: "Utilizações da frota",
  },
  "/frota": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "bus",
      label: "Frota institucional",
      placements: [
        {
          profiles: [...portariaProfiles, "Administrador"],
          section: "support",
        },
        { profiles: ["SetorTransporte"], section: "management" },
      ],
    },
    title: "Frota institucional",
  },
  "/motoristas-institucionais": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "users",
      label: "Motoristas autorizados",
      placements: [
        {
          profiles: [...portariaProfiles, "Administrador"],
          section: "support",
        },
        { profiles: ["SetorTransporte"], section: "management" },
      ],
    },
    title: "Motoristas autorizados",
  },
  "/eventos": {
    allowedProfiles: allProfiles,
    navigation: {
      icon: "calendar",
      label: "Eventos e autorizações",
      placements: [
        {
          profiles: [...portariaProfiles, "Administrador"],
          section: "support",
        },
        { profiles: ["SetorTransporte"], section: "supervision" },
      ],
    },
    title: "Eventos e autorizações",
  },
  "/administracao": {
    allowedProfiles: ["Administrador"],
    navigation: {
      icon: "users",
      label: "Usuários e permissões",
      placements: [{ profiles: ["Administrador"], section: "technical" }],
    },
    title: "Administração",
  },
};

export type ProfileCapability =
  | "correct-general-access"
  | "exceptionally-close-general-access"
  | "manage-administration"
  | "manage-event-authorizations"
  | "manage-institutional-catalogs"
  | "operate-general-access"
  | "operate-institutional-fleet"
  | "review-institutional-fleet";

const profileCapabilities: Readonly<
  Record<ProfileName, readonly ProfileCapability[]>
> = {
  Administrador: [
    "correct-general-access",
    "exceptionally-close-general-access",
    "manage-administration",
    "manage-event-authorizations",
    "manage-institutional-catalogs",
    "operate-general-access",
    "operate-institutional-fleet",
    "review-institutional-fleet",
  ],
  Porteiro: [
    "correct-general-access",
    "exceptionally-close-general-access",
    "operate-general-access",
    "operate-institutional-fleet",
  ],
  SetorTransporte: [
    "correct-general-access",
    "manage-institutional-catalogs",
    "review-institutional-fleet",
  ],
  Vigilante: [
    "correct-general-access",
    "exceptionally-close-general-access",
    "operate-general-access",
    "operate-institutional-fleet",
  ],
};

function normalizePathname(pathname: string) {
  return pathname.length > 1 ? pathname.replace(/\/+$/, "") : pathname;
}

export function canProfileAccessRoute(
  profileName: ProfileName,
  pathname: string,
) {
  const allowedProfiles =
    routeMetadata[normalizePathname(pathname)]?.allowedProfiles;

  return allowedProfiles?.includes(profileName) ?? false;
}

export function profileHasCapability(
  profileName: ProfileName,
  capability: ProfileCapability,
) {
  return profileCapabilities[profileName].includes(capability);
}

export function getProfileNavigation(
  profileName: ProfileName,
): readonly ProfileNavigationSection[] {
  const itemsBySection = new Map<
    NavigationSectionId,
    ProfileNavigationItem[]
  >();

  for (const [pathname, metadata] of Object.entries(routeMetadata)) {
    if (!metadata.navigation) continue;

    const placement = metadata.navigation.placements.find(({ profiles }) =>
      profiles.includes(profileName),
    );
    if (!placement) continue;

    const item: ProfileNavigationItem = {
      icon: metadata.navigation.icon,
      label: metadata.navigation.label,
      to: pathname as ProtectedRoutePath,
    };
    const currentItems = itemsBySection.get(placement.section) ?? [];
    currentItems.push(item);
    itemsBySection.set(placement.section, currentItems);
  }

  const sections: ProfileNavigationSection[] = [];

  for (const sectionId of profileSectionOrder[profileName]) {
    const items = itemsBySection.get(sectionId);
    if (!items?.length) continue;

    const section = sectionMetadata[sectionId];
    if (sectionId === "overview") {
      sections.push({ ...section, id: sectionId, to: items[0].to });
      continue;
    }

    sections.push({ ...section, id: sectionId, items });
  }

  return sections;
}

export function getRouteTitle(pathname: string, profileName?: ProfileName) {
  const normalizedPathname = normalizePathname(pathname);
  const metadata = routeMetadata[normalizedPathname];

  if (!metadata) return `Página não encontrada | ${applicationTitle}`;

  if (
    profileName &&
    metadata.allowedProfiles &&
    !metadata.allowedProfiles.includes(profileName)
  ) {
    return `Acesso negado | ${applicationTitle}`;
  }

  return `${metadata.title} | ${applicationTitle}`;
}

export function getRouteFocusSelector(pathname: string) {
  const normalizedPathname = normalizePathname(pathname);

  return routeMetadata[normalizedPathname]?.focusSelector ?? "main h1";
}
