import { describe, expect, it } from "vitest";

import type { ProfileName } from "../features/authentication";
import {
  canProfileAccessRoute,
  getProfileNavigation,
  profileHasCapability,
  type ProfileNavigationSection,
} from "./routeMetadata";

const profiles: readonly ProfileName[] = [
  "Porteiro",
  "Vigilante",
  "SetorTransporte",
  "Administrador",
];

function navigationSnapshot(profileName: ProfileName) {
  return getProfileNavigation(profileName).map((section) => ({
    items: section.items?.map(({ label, to }) => ({ label, to })),
    label: section.label,
    to: section.to,
  }));
}

function navigationPaths(sections: readonly ProfileNavigationSection[]) {
  return sections.flatMap((section) =>
    section.to ? [section.to] : (section.items?.map(({ to }) => to) ?? []),
  );
}

describe("profile route and navigation metadata", () => {
  it("keeps Porteiro and Vigilante navigation exactly equivalent", () => {
    expect(navigationSnapshot("Vigilante")).toEqual(
      navigationSnapshot("Porteiro"),
    );
    expect(navigationSnapshot("Porteiro")).toEqual([
      { items: undefined, label: "Visão geral", to: "/visao-geral" },
      {
        items: [
          { label: "Registrar entrada", to: "/acessos/novo" },
          { label: "Acessos em aberto", to: "/acessos/abertos" },
          { label: "Histórico de acessos", to: "/acessos/historico" },
          {
            label: "Utilizações da frota",
            to: "/utilizacoes-institucionais",
          },
        ],
        label: "Operações",
        to: undefined,
      },
      {
        items: [
          { label: "Frota institucional", to: "/frota" },
          {
            label: "Motoristas autorizados",
            to: "/motoristas-institucionais",
          },
          { label: "Eventos e autorizações", to: "/eventos" },
        ],
        label: "Consultas de apoio",
        to: undefined,
      },
    ]);
  });

  it("gives Transportation supervision and management without general operations", () => {
    expect(navigationSnapshot("SetorTransporte")).toEqual([
      { items: undefined, label: "Visão geral", to: "/visao-geral" },
      {
        items: [
          { label: "Histórico de acessos", to: "/acessos/historico" },
          {
            label: "Utilizações da frota",
            to: "/utilizacoes-institucionais",
          },
          { label: "Eventos e autorizações", to: "/eventos" },
        ],
        label: "Supervisão",
        to: undefined,
      },
      {
        items: [
          { label: "Frota institucional", to: "/frota" },
          {
            label: "Motoristas autorizados",
            to: "/motoristas-institucionais",
          },
        ],
        label: "Gestão",
        to: undefined,
      },
    ]);
  });

  it("keeps Administrator queries and technical management discoverable", () => {
    expect(navigationSnapshot("Administrador")).toEqual([
      { items: undefined, label: "Visão geral", to: "/visao-geral" },
      {
        items: [
          { label: "Histórico de acessos", to: "/acessos/historico" },
          {
            label: "Utilizações da frota",
            to: "/utilizacoes-institucionais",
          },
        ],
        label: "Supervisão",
        to: undefined,
      },
      {
        items: [
          { label: "Frota institucional", to: "/frota" },
          {
            label: "Motoristas autorizados",
            to: "/motoristas-institucionais",
          },
          { label: "Eventos e autorizações", to: "/eventos" },
        ],
        label: "Consultas de apoio",
        to: undefined,
      },
      {
        items: [{ label: "Usuários e permissões", to: "/administracao" }],
        label: "Gestão técnica",
        to: undefined,
      },
    ]);
  });

  it("uses the same source for visible navigation and direct route access", () => {
    for (const profileName of profiles) {
      for (const pathname of navigationPaths(
        getProfileNavigation(profileName),
      )) {
        expect(canProfileAccessRoute(profileName, pathname)).toBe(true);
      }
    }

    const routeExpectations: Record<ProfileName, Record<string, boolean>> = {
      Administrador: {
        "/acessos/abertos": true,
        "/acessos/historico": true,
        "/acessos/novo": true,
        "/administracao": true,
        "/conta/senha": true,
        "/eventos": true,
        "/frota": true,
        "/motoristas-institucionais": true,
        "/utilizacoes-institucionais": true,
        "/visao-geral": true,
      },
      Porteiro: {
        "/acessos/abertos": true,
        "/acessos/historico": true,
        "/acessos/novo": true,
        "/administracao": false,
        "/conta/senha": true,
        "/eventos": true,
        "/frota": true,
        "/motoristas-institucionais": true,
        "/utilizacoes-institucionais": true,
        "/visao-geral": true,
      },
      SetorTransporte: {
        "/acessos/abertos": false,
        "/acessos/historico": true,
        "/acessos/novo": false,
        "/administracao": false,
        "/conta/senha": true,
        "/eventos": true,
        "/frota": true,
        "/motoristas-institucionais": true,
        "/utilizacoes-institucionais": true,
        "/visao-geral": true,
      },
      Vigilante: {
        "/acessos/abertos": true,
        "/acessos/historico": true,
        "/acessos/novo": true,
        "/administracao": false,
        "/conta/senha": true,
        "/eventos": true,
        "/frota": true,
        "/motoristas-institucionais": true,
        "/utilizacoes-institucionais": true,
        "/visao-geral": true,
      },
    };

    for (const [profileName, paths] of Object.entries(routeExpectations) as [
      ProfileName,
      Record<string, boolean>,
    ][]) {
      for (const [pathname, expected] of Object.entries(paths)) {
        expect(canProfileAccessRoute(profileName, pathname)).toBe(expected);
      }
    }
  });

  it("preserves exceptional Administrator access without promoting it in navigation", () => {
    const administratorPaths = navigationPaths(
      getProfileNavigation("Administrador"),
    );

    expect(canProfileAccessRoute("Administrador", "/acessos/novo")).toBe(true);
    expect(canProfileAccessRoute("Administrador", "/acessos/abertos")).toBe(
      true,
    );
    expect(administratorPaths).not.toContain("/acessos/novo");
    expect(administratorPaths).not.toContain("/acessos/abertos");
  });

  it("keeps page capabilities distinct from route visibility", () => {
    expect(
      profileHasCapability("Porteiro", "manage-institutional-catalogs"),
    ).toBe(false);
    expect(
      profileHasCapability("SetorTransporte", "manage-institutional-catalogs"),
    ).toBe(true);
    expect(
      profileHasCapability("SetorTransporte", "correct-general-access"),
    ).toBe(true);
    expect(profileHasCapability("Administrador", "manage-administration")).toBe(
      true,
    );
    expect(
      profileHasCapability("Administrador", "manage-event-authorizations"),
    ).toBe(true);
    for (const profileName of [
      "Porteiro",
      "Vigilante",
      "Administrador",
    ] as const) {
      expect(
        profileHasCapability(profileName, "exceptionally-close-general-access"),
      ).toBe(true);
    }
    expect(
      profileHasCapability(
        "SetorTransporte",
        "exceptionally-close-general-access",
      ),
    ).toBe(false);
    for (const profileName of [
      "Porteiro",
      "Vigilante",
      "SetorTransporte",
    ] as const) {
      expect(
        profileHasCapability(profileName, "manage-event-authorizations"),
      ).toBe(false);
    }
  });
});
