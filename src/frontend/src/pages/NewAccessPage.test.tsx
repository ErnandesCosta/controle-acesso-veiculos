import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  registerAccessEntry,
  quickAccessObjectives,
  type AccessRecord,
  vehicleTypeOptions,
} from "../features/access-records";
import { searchAccessEntryCandidates } from "../features/access-records/services/accessRecordsService";
import {
  EventAuthorizationsContractError,
  searchEventAuthorizations,
  type EventAuthorization,
  type EventAuthorizationPage,
} from "../features/event-authorizations";
import { expectNoSeriousAccessibilityViolations } from "../test/accessibility";
import { selectFieldOption } from "../test/selectField";
import { operationalProfiles } from "../routes/routeMetadata";
import { NewAccessPage } from "./NewAccessPage";

vi.mock("../features/access-records", async () => {
  const actual = await vi.importActual<
    typeof import("../features/access-records")
  >("../features/access-records");
  return { ...actual, registerAccessEntry: vi.fn() };
});
vi.mock(
  "../features/access-records/services/accessRecordsService",
  async () => {
    const actual = await vi.importActual<
      typeof import("../features/access-records/services/accessRecordsService")
    >("../features/access-records/services/accessRecordsService");
    return { ...actual, searchAccessEntryCandidates: vi.fn() };
  },
);
vi.mock(
  "../features/event-authorizations/services/eventAuthorizationsService",
  async () => {
    const actual = await vi.importActual<
      typeof import("../features/event-authorizations/services/eventAuthorizationsService")
    >("../features/event-authorizations/services/eventAuthorizationsService");
    return { ...actual, searchEventAuthorizations: vi.fn() };
  },
);

const createdRecord: AccessRecord = {
  categoryName: "Visitante",
  createdById: 1,
  driverName: "Pessoa fictícia",
  entryAtUtc: "2026-09-02T12:00:00.000Z",
  exitAtUtc: null,
  id: 10,
  objective: "Atendimento fictício",
  observation: null,
  personId: 2,
  plate: "DEM1A23",
  status: "Aberto",
  updatedById: null,
  vehicleId: 3,
};

const recurringCandidate = {
  brand: "Marca fictícia",
  color: "Prata",
  driverName: "Condutor recorrente fictício",
  model: "Modelo fictício",
  personId: 22,
  plate: "REC1A23",
  vehicleId: 33,
  vehicleType: "Automóvel",
};

const alternateCandidate = {
  brand: "Outra marca fictícia",
  color: "Azul",
  driverName: "Outra pessoa recorrente fictícia",
  model: "Outro modelo fictício",
  personId: 44,
  plate: "ALT4B56",
  vehicleId: 55,
  vehicleType: "Van",
};

const candidateWithoutVehicleType = {
  brand: null,
  color: null,
  driverName: "Pessoa sem tipo fictícia",
  model: null,
  personId: 66,
  plate: "SEM6C78",
  vehicleId: 77,
  vehicleType: null,
};

const plateEvent: EventAuthorization = {
  active: true,
  area: "Pátio de Teste",
  createdAtUtc: "2026-01-01T12:00:00Z",
  createdById: 1,
  endsAtUtc: "2100-01-01T00:00:00Z",
  id: 7,
  name: "Evento por Placa Fictício",
  notes: null,
  overnightAllowed: false,
  responsible: "Setor Fictício",
  startsAtUtc: "2020-01-01T00:00:00Z",
  updatedAtUtc: null,
  updatedById: null,
  vehicleRules: [
    {
      consumedQuantity: 0,
      id: 9,
      plate: "EVT1A23",
      quantity: 1,
      remainingQuantity: 1,
      vehicleType: "AUTOMÓVEL",
    },
  ],
};

const quotaEvent: EventAuthorization = {
  ...plateEvent,
  id: 8,
  name: "Evento por Cota Fictício",
  vehicleRules: [
    {
      consumedQuantity: 2,
      id: 10,
      plate: null,
      quantity: 5,
      remainingQuantity: 3,
      vehicleType: "VAN",
    },
  ],
};

function eventPage(items: EventAuthorization[]): EventAuthorizationPage {
  return {
    items,
    page: 1,
    pageSize: 100,
    totalCount: items.length,
    totalPages: items.length > 0 ? 1 : 0,
  };
}

function apiError(status: number, data: unknown) {
  return { isAxiosError: true, response: { data, status } };
}

function renderPage({ strictMode = false } = {}) {
  const page = (
    <MemoryRouter initialEntries={["/acessos/novo"]}>
      <Routes>
        <Route path="/acessos/novo" element={<NewAccessPage />} />
        <Route path="/acessos/abertos" element={<h1>Acessos carregados</h1>} />
      </Routes>
    </MemoryRouter>
  );
  return render(strictMode ? <StrictMode>{page}</StrictMode> : page);
}

type TestUser = ReturnType<typeof userEvent.setup>;

async function fillRequiredFields(
  user: TestUser,
  plate = "DEM-1A23",
  driverName = "Pessoa fictícia",
) {
  await user.type(screen.getByLabelText(/Placa do veículo/), plate);
  await user.type(screen.getByLabelText(/Nome do condutor/), driverName);
  await user.click(screen.getByRole("radio", { name: "Atendimento em setor" }));
}

async function chooseCustomObjective(user: TestUser, value: string) {
  const otherOption = screen.getByRole("radio", { name: "Outro" });
  await user.click(otherOption);
  expect(otherOption).toBeChecked();
  const field = await screen.findByLabelText(/Outro objetivo/);
  await user.type(field, value);
  return field;
}

async function fillObservation(user: TestUser, value: string) {
  await user.click(screen.getByText("Detalhes adicionais"));
  const field = screen.getByLabelText("Observação");
  await user.type(field, value);
  return field;
}

async function advanceCandidateSearchDebounce() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(350);
  });
}

describe("NewAccessPage", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(searchEventAuthorizations).mockResolvedValue(
      eventPage([plateEvent, quotaEvent]),
    );
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([]);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("fills and submits both canonical candidate identifiers together", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      recurringCandidate,
    ]);
    vi.mocked(registerAccessEntry).mockResolvedValue({
      ...createdRecord,
      driverName: recurringCandidate.driverName,
      personId: recurringCandidate.personId,
      plate: recurringCandidate.plate,
      vehicleId: recurringCandidate.vehicleId,
    });
    const user = userEvent.setup();
    renderPage();

    await user.type(
      screen.getByRole("combobox", {
        name: "Buscar por placa ou nome do condutor",
      }),
      "REC",
    );
    await user.click(await screen.findByRole("option", { name: /REC1A23/ }));
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("REC1A23");
    expect(screen.getByLabelText(/Nome do condutor/)).toHaveValue(
      recurringCandidate.driverName,
    );
    expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent(
      "Automóvel",
    );
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
    expect(registerAccessEntry).toHaveBeenCalledWith({
      categoryName: "Visitante",
      driverName: recurringCandidate.driverName,
      objective: "Atendimento em setor",
      observation: undefined,
      personId: 22,
      plate: "REC1A23",
      vehicleId: 33,
      vehicleType: "Automóvel",
    });
    await waitFor(() =>
      expect(screen.getByLabelText(/Placa do veículo/)).toHaveFocus(),
    );
    expect(screen.queryByText(/Dados recuperados/)).not.toBeInTheDocument();
  });

  it("invalidates candidate identifiers after a manual plate edit", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      recurringCandidate,
    ]);
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await user.type(
      screen.getByRole("combobox", {
        name: "Buscar por placa ou nome do condutor",
      }),
      "REC",
    );
    await user.click(await screen.findByRole("option", { name: /REC1A23/ }));
    const plate = screen.getByLabelText(/Placa do veículo/);
    await user.type(plate, "9");
    expect(screen.getByLabelText(/Nome do condutor/)).toHaveValue("");
    expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent(
      "Não informado",
    );
    expect(screen.queryByText(/Dados recuperados/)).not.toBeInTheDocument();
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Nova pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
    expect(registerAccessEntry).toHaveBeenCalledWith(
      expect.not.objectContaining({ personId: 22, vehicleId: 33 }),
    );
    expect(registerAccessEntry).toHaveBeenCalledWith(
      expect.objectContaining({
        driverName: "Nova pessoa fictícia",
        plate: "REC1A239",
        vehicleType: undefined,
      }),
    );
  });

  it("clears a candidate without losing unrelated entry values", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      recurringCandidate,
    ]);
    const user = userEvent.setup();
    renderPage();

    await selectFieldOption(
      user,
      screen.getByLabelText(/Categoria do acesso/),
      "Entrega",
    );
    await user.click(screen.getByRole("radio", { name: "Reunião" }));
    const observation = await fillObservation(user, "Observação fictícia");
    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    const eventOption = await screen.findByRole("radio", {
      name: /Evento por Placa Fictício/,
    });
    await user.click(eventOption);
    await user.type(
      screen.getByRole("combobox", {
        name: "Buscar por placa ou nome do condutor",
      }),
      "REC",
    );
    await user.click(await screen.findByRole("option", { name: /REC1A23/ }));
    await user.click(
      screen.getByRole("button", { name: "Usar preenchimento manual" }),
    );

    expect(screen.getByLabelText(/Categoria do acesso/)).toHaveTextContent(
      "Entrega",
    );
    expect(screen.getByRole("radio", { name: "Reunião" })).toBeChecked();
    expect(observation).toHaveValue("Observação fictícia");
    expect(eventOption).toBeChecked();
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("");
    expect(screen.getByLabelText(/Nome do condutor/)).toHaveValue("");
    expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent(
      "Não informado",
    );
    await waitFor(() =>
      expect(screen.getByLabelText(/Placa do veículo/)).toHaveFocus(),
    );
    expect(screen.queryByText(/Dados recuperados/)).not.toBeInTheDocument();
  });

  it("keeps the current candidate while searching and replaces it only after selection", async () => {
    vi.mocked(searchAccessEntryCandidates)
      .mockResolvedValueOnce([recurringCandidate])
      .mockResolvedValueOnce([alternateCandidate]);
    const user = userEvent.setup();
    renderPage();
    const search = screen.getByRole("combobox", {
      name: "Buscar por placa ou nome do condutor",
    });

    await user.click(search);
    vi.useFakeTimers();
    fireEvent.change(search, { target: { value: "REC" } });
    await advanceCandidateSearchDebounce();
    vi.useRealTimers();
    await user.click(await screen.findByRole("option", { name: /REC1A23/ }));
    expect(screen.getByText(/REC1A23 · Condutor recorrente/)).toBeVisible();

    await user.click(search);
    vi.useFakeTimers();
    fireEvent.change(search, { target: { value: "ALT" } });
    await advanceCandidateSearchDebounce();
    vi.useRealTimers();
    const alternateOption = await screen.findByRole("option", {
      name: /ALT4B56/,
    });
    expect(screen.getByText(/REC1A23 · Condutor recorrente/)).toBeVisible();
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("REC1A23");

    await user.click(alternateOption);
    expect(
      screen.queryByText(/REC1A23 · Condutor recorrente/),
    ).not.toBeInTheDocument();
    expect(screen.getByText(/ALT4B56 · Outra pessoa recorrente/)).toBeVisible();
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("ALT4B56");
    expect(screen.getByLabelText(/Nome do condutor/)).toHaveValue(
      alternateCandidate.driverName,
    );
    expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent("Van");
  });

  it("clears a previous vehicle type when a candidate has no type", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      candidateWithoutVehicleType,
    ]);
    const user = userEvent.setup();
    renderPage();

    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Van",
    );
    const search = screen.getByRole("combobox", {
      name: "Buscar por placa ou nome do condutor",
    });
    await user.click(search);
    vi.useFakeTimers();
    fireEvent.change(search, { target: { value: "SEM" } });
    await advanceCandidateSearchDebounce();
    vi.useRealTimers();
    await user.click(await screen.findByRole("option", { name: /SEM6C78/ }));

    expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent(
      "Não informado",
    );
    expect(
      screen.queryByLabelText(/Outro tipo de veículo/),
    ).not.toBeInTheDocument();
  });

  it("does not submit a valid entry when Enter has no active search option", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      recurringCandidate,
    ]);
    const user = userEvent.setup();
    renderPage();

    await fillRequiredFields(user);
    const search = screen.getByRole("combobox", {
      name: "Buscar por placa ou nome do condutor",
    });
    await user.type(search, "REC");
    await screen.findByRole("option", { name: /REC1A23/ });
    await user.keyboard("{Enter}");

    expect(registerAccessEntry).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("DEM-1A23");
  });

  it("preserves a selected candidate after a submission conflict", async () => {
    vi.mocked(searchAccessEntryCandidates).mockResolvedValue([
      recurringCandidate,
    ]);
    let rejectRequest: ((reason: unknown) => void) | undefined;
    vi.mocked(registerAccessEntry).mockImplementation(
      () =>
        new Promise((_, reject) => {
          rejectRequest = reject;
        }),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(
      screen.getByRole("combobox", {
        name: "Buscar por placa ou nome do condutor",
      }),
      "REC",
    );
    await user.click(await screen.findByRole("option", { name: /REC1A23/ }));
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    await user.dblClick(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );
    expect(registerAccessEntry).toHaveBeenCalledTimes(1);
    rejectRequest?.(
      apiError(409, {
        errors: {
          accessRecord: [
            "O veículo ou condutor selecionado não está mais disponível.",
          ],
        },
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "não está mais disponível",
    );
    expect(registerAccessEntry).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/Dados recuperados/)).toBeVisible();
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("REC1A23");
  });

  it("uses primary ink throughout the solid quick-check surface", () => {
    renderPage();

    const quickCheck = screen
      .getByText("Conferência rápida")
      .closest("section");

    expect(quickCheck).toHaveClass("bg-[#B8C9A4]", "text-ink");
    expect(quickCheck?.querySelector(".text-ink-soft")).toBeNull();
    expect(
      screen.getByRole("link", { name: "Utilizações da frota" }),
    ).toHaveAttribute("href", "/utilizacoes-institucionais");
    expect(
      screen.getByRole("link", { name: "Utilizações da frota" }),
    ).toHaveClass("font-bold", "text-ink");
  });

  it("submits documented fields once and opens accesses when requested", async () => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await fillRequiredFields(user);
    await user.click(
      screen.getByRole("button", { name: "Registrar e ver acessos" }),
    );

    await waitFor(() =>
      expect(registerAccessEntry).toHaveBeenCalledWith({
        categoryName: "Visitante",
        driverName: "Pessoa fictícia",
        objective: "Atendimento em setor",
        observation: undefined,
        plate: "DEM-1A23",
        vehicleType: undefined,
      }),
    );
    expect(
      await screen.findByRole("heading", { name: "Acessos carregados" }),
    ).toBeInTheDocument();
  });

  it("identifies the unlinked event choice as the default", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );

    expect(
      await screen.findByRole("radio", { name: /Sem autorização de evento/ }),
    ).toBeChecked();
    expect(screen.getByText("Padrão")).toBeInTheDocument();
    expect(screen.getAllByText("Veículos autorizados")).toHaveLength(2);
  });

  it.each([
    ["Evento por Placa Fictício", 7],
    ["Evento por Cota Fictício", 8],
  ])("associates an entry explicitly with %s", async (eventName, eventId) => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    await user.click(
      await screen.findByRole("radio", { name: new RegExp(eventName) }),
    );
    await user.type(screen.getByLabelText(/Placa do veículo/), "EVT-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Automóvel",
    );
    await user.click(
      screen.getByRole("radio", { name: "Participação em atividade" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    await waitFor(() =>
      expect(registerAccessEntry).toHaveBeenCalledWith(
        expect.objectContaining({ eventAuthorizationId: eventId }),
      ),
    );
  });

  it("keeps a common entry available when the event query fails", async () => {
    vi.mocked(searchEventAuthorizations).mockRejectedValue(
      new Error("network"),
    );
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      /entrada comum sem vincular um evento/i,
    );
    await user.type(screen.getByLabelText(/Placa do veículo/), "COM-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
    expect(registerAccessEntry).toHaveBeenCalledWith(
      expect.not.objectContaining({ eventAuthorizationId: expect.anything() }),
    );
    expect(searchEventAuthorizations).toHaveBeenCalledTimes(1);
  });

  it("distinguishes an empty event list and retries its query", async () => {
    vi.mocked(searchEventAuthorizations)
      .mockResolvedValueOnce(eventPage([]))
      .mockResolvedValueOnce(eventPage([plateEvent]));
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    expect(
      await screen.findByText("Nenhuma autorização vigente disponível"),
    ).toBeInTheDocument();
    await user.click(
      screen.getByRole("button", { name: "Atualizar autorizações" }),
    );

    expect(
      await screen.findByRole("radio", { name: /Evento por Placa Fictício/ }),
    ).toBeInTheDocument();
    expect(registerAccessEntry).not.toHaveBeenCalled();
  });

  it("presents an invalid event response separately", async () => {
    vi.mocked(searchEventAuthorizations).mockRejectedValue(
      new EventAuthorizationsContractError(),
    );
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /resposta das autorizações não pôde ser validada/i,
    );
  });

  it("presents denied event lookup without disabling the common form", async () => {
    vi.mocked(searchEventAuthorizations).mockRejectedValue(apiError(403, {}));
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /perfil não possui permissão/i,
    );
    expect(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    ).toBeEnabled();
  });

  it("resets the completed entry and restores plate focus for the next vehicle", async () => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    const plateField = screen.getByLabelText(/Placa do veículo/);
    const driverField = screen.getByLabelText(/Nome do condutor/);
    const objectiveField = screen.getByRole("radio", {
      name: "Atendimento em setor",
    });

    await user.type(plateField, "DEM-1A23");
    await user.type(driverField, "Pessoa fictícia");
    await user.click(objectiveField);
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(
      await screen.findByText(
        "Entrada registrada. O formulário está pronto para o próximo veículo.",
      ),
    ).toHaveAttribute("role", "status");
    expect(plateField).toHaveValue("");
    expect(driverField).toHaveValue("");
    expect(objectiveField).not.toBeChecked();
    await waitFor(() => expect(plateField).toHaveFocus());
    expect(
      screen.getByRole("heading", { name: "Registrar entrada" }),
    ).toBeInTheDocument();
  });

  it("clears the completed event association for the next entry", async () => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    await user.click(
      await screen.findByRole("radio", { name: /Evento por Placa Fictício/ }),
    );
    await user.type(screen.getByLabelText(/Placa do veículo/), "EVT-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Participação em atividade" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );
    await screen.findByText(/formulário está pronto/);

    expect(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    ).toHaveAttribute("aria-expanded", "false");
    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    expect(
      screen.getByRole("radio", { name: /Sem autorização de evento/ }),
    ).toBeChecked();
  });

  it("uses the consecutive flow when the form is submitted with Enter", async () => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    const plateField = screen.getByLabelText(/Placa do veículo/);
    await user.type(plateField, "DEM-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    const consecutiveButton = screen.getByRole("button", {
      name: "Registrar e continuar",
    });
    consecutiveButton.focus();
    await user.keyboard("{Enter}");

    expect(await screen.findByText(/formulário está pronto/)).toHaveAttribute(
      "role",
      "status",
    );
    await waitFor(() => expect(plateField).toHaveFocus());
    expect(
      screen.getByRole("heading", { name: "Registrar entrada" }),
    ).toBeInTheDocument();
  });

  it("removes an earlier success when the next entry fails and keeps its values", async () => {
    vi.mocked(registerAccessEntry)
      .mockResolvedValueOnce(createdRecord)
      .mockRejectedValueOnce(new Error("network"));
    const user = userEvent.setup();
    renderPage();

    const plateField = screen.getByLabelText(/Placa do veículo/);
    const driverField = screen.getByLabelText(/Nome do condutor/);
    const firstObjective = screen.getByRole("radio", {
      name: "Atendimento em setor",
    });

    await user.type(plateField, "DEM-1A23");
    await user.type(driverField, "Pessoa fictícia");
    await user.click(firstObjective);
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );
    await screen.findByText(/formulário está pronto/);

    await user.type(plateField, "TST-2B34");
    await user.type(driverField, "Outra pessoa fictícia");
    const objectiveField = await chooseCustomObjective(
      user,
      "Segundo atendimento",
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(
      screen.queryByText(/formulário está pronto/),
    ).not.toBeInTheDocument();
    expect(plateField).toHaveValue("TST-2B34");
    expect(driverField).toHaveValue("Outra pessoa fictícia");
    expect(objectiveField).toHaveValue("Segundo atendimento");
  });

  it.each([
    "A autorização de evento informada não foi encontrada.",
    "A autorização de evento está cancelada.",
    "A entrada está fora da janela autorizada do evento.",
    "A placa ou o tipo do veículo não está autorizado para o evento.",
    "A cota de veículos do evento foi atingida.",
  ])(
    "preserves the form and presents the event conflict: %s",
    async (message) => {
      vi.mocked(registerAccessEntry).mockRejectedValue(
        apiError(409, {
          errors: { accessRecord: [message] },
          message: "Não foi possível registrar a entrada.",
        }),
      );
      const user = userEvent.setup();
      renderPage();

      await user.click(
        screen.getByRole("button", {
          name: "Vincular uma autorização de evento",
        }),
      );
      const eventRadio = await screen.findByRole("radio", {
        name: /Evento por Placa Fictício/,
      });
      await user.click(eventRadio);
      await user.type(screen.getByLabelText(/Placa do veículo/), "EVT-1A23");
      await user.type(
        screen.getByLabelText(/Nome do condutor/),
        "Pessoa fictícia",
      );
      await selectFieldOption(
        user,
        screen.getByLabelText(/Tipo do veículo/),
        "Automóvel",
      );
      await user.click(
        screen.getByRole("radio", { name: "Participação em atividade" }),
      );
      await fillObservation(user, "Observação fictícia");
      await user.click(
        screen.getByRole("button", { name: "Registrar e continuar" }),
      );

      expect(await screen.findByRole("alert")).toHaveTextContent(message);
      expect(screen.getByLabelText(/Placa do veículo/)).toHaveValue("EVT-1A23");
      expect(screen.getByLabelText(/Nome do condutor/)).toHaveValue(
        "Pessoa fictícia",
      );
      expect(screen.getByLabelText(/Tipo do veículo/)).toHaveTextContent(
        "Automóvel",
      );
      expect(
        screen.getByRole("radio", { name: "Participação em atividade" }),
      ).toBeChecked();
      expect(screen.getByLabelText(/Observação/)).toHaveValue(
        "Observação fictícia",
      );
      expect(eventRadio).toBeChecked();
      expect(registerAccessEntry).toHaveBeenCalledTimes(1);
      expect(
        screen.queryByText(/formulário está pronto/),
      ).not.toBeInTheDocument();
    },
  );

  it("associates an event identifier error with the authorization group", async () => {
    vi.mocked(registerAccessEntry).mockRejectedValue(
      apiError(400, {
        errors: {
          eventAuthorizationId: ["Selecione uma autorização válida."],
        },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    await user.click(
      await screen.findByRole("radio", { name: /Evento por Placa Fictício/ }),
    );
    await user.type(screen.getByLabelText(/Placa do veículo/), "EVT-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Participação em atividade" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    const group = await screen.findByRole("group", {
      name: "Autorizações vigentes",
    });
    expect(group).toHaveAttribute("aria-invalid", "true");
    expect(group).toHaveAttribute(
      "aria-describedby",
      expect.stringContaining("eventAuthorizationId-error"),
    );
    expect(
      document.getElementById("eventAuthorizationId-error"),
    ).toHaveTextContent("Selecione uma autorização válida.");
  });

  it("blocks both completion paths while an entry request is pending", async () => {
    let resolveRequest: ((record: AccessRecord) => void) | undefined;
    vi.mocked(registerAccessEntry).mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveRequest = resolve;
        }),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/Placa do veículo/), "DEM-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    );
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(screen.getByRole("button", { name: "Registrando…" })).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "Registrar e ver acessos" }),
    ).toBeDisabled();
    expect(registerAccessEntry).toHaveBeenCalledTimes(1);

    resolveRequest?.(createdRecord);
    await screen.findByText(/formulário está pronto/);
  });

  it("keeps invalid data in the browser and identifies required fields", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(
      await screen.findByText("Informe a placa do veículo."),
    ).toBeInTheDocument();
    expect(screen.getByText("Informe o nome do condutor.")).toBeInTheDocument();
    expect(
      screen.getByText("Selecione o objetivo do acesso."),
    ).toBeInTheDocument();
    const objectiveGroup = screen.getByRole("radiogroup", {
      name: /Objetivo do acesso/,
    });
    expect(objectiveGroup.closest("fieldset")).toHaveAttribute(
      "aria-describedby",
      "objective-error",
    );
    expect(
      screen.getByRole("radio", { name: "Atendimento em setor" }),
    ).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText(/Placa do veículo/)).toHaveAttribute(
      "aria-describedby",
      "plate-error",
    );
    expect(document.getElementById("plate-error")).toHaveTextContent(
      "Informe a placa do veículo.",
    );
    expect(registerAccessEntry).not.toHaveBeenCalled();
  });

  it.each(quickAccessObjectives.filter((objective) => objective !== "Outro"))(
    "sends the quick objective %s without changing the category",
    async (objective) => {
      vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
      const user = userEvent.setup();
      renderPage();

      await user.type(screen.getByLabelText(/Placa do veículo/), "OBJ-1A23");
      await user.type(
        screen.getByLabelText(/Nome do condutor/),
        "Pessoa fictícia",
      );
      await selectFieldOption(
        user,
        screen.getByLabelText(/Categoria do acesso/),
        "Entrega",
      );
      await user.click(screen.getByRole("radio", { name: objective }));
      await user.click(
        screen.getByRole("button", { name: "Registrar e continuar" }),
      );

      await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
      expect(registerAccessEntry).toHaveBeenCalledWith(
        expect.objectContaining({ categoryName: "Entrega", objective }),
      );
    },
  );

  it.each(vehicleTypeOptions.filter((vehicleType) => vehicleType !== "Outro"))(
    "sends the known vehicle type %s exactly as selected",
    async (vehicleType) => {
      vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
      const user = userEvent.setup();
      renderPage();

      await fillRequiredFields(user, "TIP-1A23");
      await selectFieldOption(
        user,
        screen.getByLabelText(/Tipo do veículo/),
        vehicleType,
      );
      await user.click(
        screen.getByRole("button", { name: "Registrar e continuar" }),
      );

      await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
      expect(registerAccessEntry).toHaveBeenCalledWith(
        expect.objectContaining({ vehicleType }),
      );
    },
  );

  it("sends custom objective and vehicle type values in the current API contract", async () => {
    vi.mocked(registerAccessEntry).mockResolvedValue(createdRecord);
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/Placa do veículo/), "CUS-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await chooseCustomObjective(user, "Visita técnica fictícia");
    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Outro",
    );
    const customVehicleType = await screen.findByLabelText(
      /Outro tipo de veículo/,
    );
    await user.type(customVehicleType, "Triciclo fictício");
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    await waitFor(() => expect(registerAccessEntry).toHaveBeenCalledTimes(1));
    expect(registerAccessEntry).toHaveBeenCalledWith({
      categoryName: "Visitante",
      driverName: "Pessoa fictícia",
      objective: "Visita técnica fictícia",
      observation: undefined,
      plate: "CUS-1A23",
      vehicleType: "Triciclo fictício",
    });
  });

  it("associates conditional validation errors and focuses revealed fields", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/Placa do veículo/), "VAL-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    await user.click(screen.getByRole("radio", { name: "Outro" }));
    const objectiveOther = await screen.findByLabelText(/Outro objetivo/);
    await waitFor(() => expect(objectiveOther).toHaveFocus());
    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Outro",
    );
    const vehicleTypeOther = await screen.findByLabelText(
      /Outro tipo de veículo/,
    );
    await waitFor(() => expect(vehicleTypeOther).toHaveFocus());
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(objectiveOther).toHaveAttribute("aria-invalid", "true");
    expect(objectiveOther).toHaveAttribute(
      "aria-describedby",
      "objectiveOther-error",
    );
    expect(vehicleTypeOther).toHaveAttribute("aria-invalid", "true");
    expect(vehicleTypeOther).toHaveAttribute(
      "aria-describedby",
      "vehicleTypeOther-error",
    );
    expect(registerAccessEntry).not.toHaveBeenCalled();
  });

  it("moves focus to the conditional vehicle type and back using only the keyboard", async () => {
    const user = userEvent.setup();
    renderPage({ strictMode: true });

    const vehicleType = screen.getByLabelText(/Tipo do veículo/);
    vehicleType.focus();
    await user.keyboard("{Enter}Outro{Enter}");

    const vehicleTypeOther = await screen.findByLabelText(
      /Outro tipo de veículo/,
    );
    expect(vehicleTypeOther).toHaveFocus();
    await user.type(vehicleTypeOther, "Triciclo fictício");

    await user.tab({ shift: true });
    expect(vehicleType).toHaveFocus();
    await user.keyboard("{Enter}Automóvel{Enter}");

    expect(vehicleType).toHaveFocus();
    expect(vehicleType).toHaveAttribute("data-value", vehicleTypeOptions[0]);
    expect(
      screen.queryByLabelText(/Outro tipo de veículo/),
    ).not.toBeInTheDocument();
  });

  it("associates API errors with the active custom fields", async () => {
    vi.mocked(registerAccessEntry).mockRejectedValue(
      apiError(400, {
        errors: {
          objective: ["Revise o objetivo informado."],
          vehicleType: ["Revise o tipo informado."],
        },
      }),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/Placa do veículo/), "API-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    const objectiveOther = await chooseCustomObjective(
      user,
      "Objetivo fictício",
    );
    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Outro",
    );
    const vehicleTypeOther = await screen.findByLabelText(
      /Outro tipo de veículo/,
    );
    await user.type(vehicleTypeOther, "Tipo fictício");
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(
      await screen.findByText("Revise o objetivo informado."),
    ).toHaveAttribute("id", "objectiveOther-error");
    expect(objectiveOther).toHaveAttribute(
      "aria-describedby",
      "objectiveOther-error",
    );
    expect(screen.getByText("Revise o tipo informado.")).toHaveAttribute(
      "id",
      "vehicleTypeOther-error",
    );
    expect(vehicleTypeOther).toHaveAttribute(
      "aria-describedby",
      "vehicleTypeOther-error",
    );
  });

  it("keeps additional details mounted while the section is collapsed", async () => {
    const user = userEvent.setup();
    renderPage();

    const observation = await fillObservation(user, "Observação fictícia");
    await user.click(screen.getByText("Detalhes adicionais"));
    expect(screen.getByText("Detalhes adicionais")).toHaveAttribute(
      "aria-expanded",
      "false",
    );
    await user.click(screen.getByText("Detalhes adicionais"));
    expect(observation).toHaveValue("Observação fictícia");
  });

  it("preserves custom values and details after a network failure", async () => {
    vi.mocked(registerAccessEntry).mockRejectedValue(new Error("network"));
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText(/Placa do veículo/), "NET-1A23");
    await user.type(
      screen.getByLabelText(/Nome do condutor/),
      "Pessoa fictícia",
    );
    const objectiveOther = await chooseCustomObjective(
      user,
      "Objetivo fictício personalizado",
    );
    await selectFieldOption(
      user,
      screen.getByLabelText(/Tipo do veículo/),
      "Outro",
    );
    const vehicleTypeOther = await screen.findByLabelText(
      /Outro tipo de veículo/,
    );
    await user.type(vehicleTypeOther, "Veículo fictício adaptado");
    const observation = await fillObservation(user, "Detalhe fictício");
    await user.click(
      screen.getByRole("button", { name: "Registrar e continuar" }),
    );

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(objectiveOther).toHaveValue("Objetivo fictício personalizado");
    expect(vehicleTypeOther).toHaveValue("Veículo fictício adaptado");
    expect(observation).toHaveValue("Detalhe fictício");
    expect(registerAccessEntry).toHaveBeenCalledTimes(1);
  });

  it("supports keyboard selection without adding conditional fields to the default tab order", async () => {
    const user = userEvent.setup();
    renderPage();

    const firstObjective = screen.getByRole("radio", {
      name: "Atendimento em setor",
    });
    firstObjective.focus();
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("radio", { name: "Reunião" })).toBeChecked();

    const details = screen.getByText("Detalhes adicionais");
    details.focus();
    await user.keyboard("{Enter}");
    expect(details).toHaveAttribute("aria-expanded", "true");
  });

  it("keeps the general entry route identical for Porteiro and Vigilante", () => {
    expect(operationalProfiles).toEqual(
      expect.arrayContaining(["Porteiro", "Vigilante"]),
    );
    expect(operationalProfiles.indexOf("Porteiro") + 1).toBe(
      operationalProfiles.indexOf("Vigilante"),
    );
  });

  it("has no serious automated accessibility violations", async () => {
    const { container } = renderPage();

    await expectNoSeriousAccessibilityViolations(container);
  });

  it("has no serious accessibility violations with event choices visible", async () => {
    const user = userEvent.setup();
    const { container } = renderPage();

    await user.click(
      screen.getByRole("button", {
        name: "Vincular uma autorização de evento",
      }),
    );
    await screen.findByRole("radio", { name: /Evento por Placa Fictício/ });

    await expectNoSeriousAccessibilityViolations(container);
  });
});
