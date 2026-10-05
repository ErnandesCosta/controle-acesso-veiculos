import { Navigate, createBrowserRouter } from "react-router-dom";

import { AppLayout } from "../components/layout/AppLayout";
import { LoginPage } from "../pages/LoginPage";
import { NotFoundPage } from "../pages/NotFoundPage";
import { ProfileRoute } from "./ProfileRoute";
import { ProtectedRoute } from "./ProtectedRoute";
import { RouteTransitionManager } from "./RouteTransitionManager";

const loadAdminPage = () =>
  import("../pages/AdminPage").then(({ AdminPage }) => ({
    Component: AdminPage,
  }));
const loadDashboardPage = () =>
  import("../pages/DashboardPage").then(({ DashboardPage }) => ({
    Component: DashboardPage,
  }));
const loadEventsPage = () =>
  import("../pages/EventsPage").then(({ EventsPage }) => ({
    Component: EventsPage,
  }));
const loadFleetPage = () =>
  import("../pages/FleetPage").then(({ FleetPage }) => ({
    Component: FleetPage,
  }));
const loadHistoryPage = () =>
  import("../pages/HistoryPage").then(({ HistoryPage }) => ({
    Component: HistoryPage,
  }));
const loadInstitutionalDriversPage = () =>
  import("../pages/InstitutionalDriversPage").then(
    ({ InstitutionalDriversPage }) => ({
      Component: InstitutionalDriversPage,
    }),
  );
const loadInstitutionalUsagesPage = () =>
  import("../pages/InstitutionalUsagesPage").then(
    ({ InstitutionalUsagesPage }) => ({
      Component: InstitutionalUsagesPage,
    }),
  );
const loadNewAccessPage = () =>
  import("../pages/NewAccessPage").then(({ NewAccessPage }) => ({
    Component: NewAccessPage,
  }));
const loadOpenAccessPage = () =>
  import("../pages/OpenAccessPage").then(({ OpenAccessPage }) => ({
    Component: OpenAccessPage,
  }));
const loadPasswordChangePage = () =>
  import("../pages/PasswordChangePage").then(({ PasswordChangePage }) => ({
    Component: PasswordChangePage,
  }));

export const router = createBrowserRouter([
  {
    element: <RouteTransitionManager />,
    children: [
      {
        path: "/",
        element: <Navigate replace to="/login" />,
      },
      {
        path: "/login",
        element: <LoginPage />,
      },
      {
        element: <ProtectedRoute />,
        children: [
          {
            element: <AppLayout />,
            children: [
              {
                element: <ProfileRoute />,
                children: [
                  { path: "/visao-geral", lazy: loadDashboardPage },
                  { path: "/conta/senha", lazy: loadPasswordChangePage },
                  { path: "/acessos/novo", lazy: loadNewAccessPage },
                  { path: "/acessos/abertos", lazy: loadOpenAccessPage },
                  { path: "/acessos/historico", lazy: loadHistoryPage },
                  {
                    path: "/utilizacoes-institucionais",
                    lazy: loadInstitutionalUsagesPage,
                  },
                  { path: "/frota", lazy: loadFleetPage },
                  {
                    path: "/motoristas-institucionais",
                    lazy: loadInstitutionalDriversPage,
                  },
                  { path: "/eventos", lazy: loadEventsPage },
                  { path: "/administracao", lazy: loadAdminPage },
                ],
              },
            ],
          },
        ],
      },
      {
        path: "*",
        element: <NotFoundPage />,
      },
    ],
  },
]);
