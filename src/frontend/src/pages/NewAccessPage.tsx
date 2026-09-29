import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { Link, useNavigate } from "react-router-dom";

import { Icon } from "../components/ui/Icon";
import { PageHeader } from "../components/ui/PageHeader";
import { SelectField } from "../components/ui/SelectField";
import {
  accessEntryFormSchema,
  customEntryOption,
  EntryAdditionalDetails,
  EntryCandidateSearch,
  EntryObjectiveFieldset,
  EntryVehicleTypeField,
  generalAccessCategories,
  registerAccessEntry,
  type AccessEntryFormValues,
  type AccessEntryCandidate,
  type RegisterAccessEntryInput,
  vehicleTypeOptions,
} from "../features/access-records";
import {
  EventAuthorizationSelector,
  useCurrentEventAuthorizations,
  type EventAuthorization,
} from "../features/event-authorizations";
import {
  describeApiError,
  getApiValidationErrors,
} from "../services/api-errors";

const fieldClass =
  "mt-2 min-h-12 w-full rounded-xl border border-ink/20 bg-cream/55 px-4 text-ink outline-none transition placeholder:text-ink-soft focus:border-brand-dark focus:bg-white focus:ring-3 focus:ring-brand/20";

const fieldNames: Record<string, keyof AccessEntryFormValues> = {
  eventAuthorizationId: "eventAuthorizationId",
  plate: "plate",
  driverName: "driverName",
  categoryName: "categoryName",
  observation: "observation",
};

const defaultValues: AccessEntryFormValues = {
  categoryName: generalAccessCategories[0],
  driverName: "",
  eventAuthorizationId: "",
  objective: "",
  objectiveOther: "",
  observation: "",
  plate: "",
  vehicleType: "",
  vehicleTypeOther: "",
};

type SubmitIntent = "continue" | "review";

function FieldError({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p className="mt-1.5 text-sm text-red-800" id={id}>
      {message}
    </p>
  );
}

export function NewAccessPage() {
  const navigate = useNavigate();
  const [eventSectionOpen, setEventSectionOpen] = useState(false);
  const [additionalDetailsOpen, setAdditionalDetailsOpen] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);
  const [pendingIntent, setPendingIntent] = useState<SubmitIntent | null>(null);
  const [selectedCandidate, setSelectedCandidate] =
    useState<AccessEntryCandidate | null>(null);
  const {
    formState: { errors, isSubmitting },
    control,
    handleSubmit,
    register,
    reset,
    clearErrors,
    setError,
    setFocus,
    setValue,
  } = useForm<AccessEntryFormValues>({
    defaultValues,
    resolver: zodResolver(accessEntryFormSchema),
  });
  const eventAuthorizations = useCurrentEventAuthorizations(eventSectionOpen);
  const selectedEventAuthorizationId = useWatch({
    control,
    name: "eventAuthorizationId",
  });
  const selectedVehicleType = useWatch({ control, name: "vehicleType" });
  const selectedEvent = eventAuthorizations.events.find(
    (event) => String(event.id) === selectedEventAuthorizationId,
  );

  function clearSelectedCandidateAfterEdit(
    editedField: "plate" | "driverName",
  ) {
    if (!selectedCandidate) return;
    setSelectedCandidate(null);
    setValue(editedField === "plate" ? "driverName" : "plate", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    setValue("vehicleType", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    setValue("vehicleTypeOther", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
  }

  function useManualEntry() {
    setSelectedCandidate(null);
    setValue("plate", "", { shouldDirty: true, shouldValidate: false });
    setValue("driverName", "", { shouldDirty: true, shouldValidate: false });
    setValue("vehicleType", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    setValue("vehicleTypeOther", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    clearErrors(["plate", "driverName", "vehicleType", "vehicleTypeOther"]);
    window.requestAnimationFrame(() =>
      document.getElementById("plate")?.focus(),
    );
  }

  function selectCandidate(candidate: AccessEntryCandidate) {
    setSelectedCandidate(candidate);
    clearErrors(["plate", "driverName", "vehicleType", "vehicleTypeOther"]);
    setValue("plate", candidate.plate, {
      shouldDirty: true,
      shouldValidate: true,
    });
    setValue("driverName", candidate.driverName, {
      shouldDirty: true,
      shouldValidate: true,
    });

    setValue("vehicleType", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    setValue("vehicleTypeOther", "", {
      shouldDirty: true,
      shouldValidate: false,
    });
    if (!candidate.vehicleType) return;
    const knownVehicleType = vehicleTypeOptions.find(
      (option) =>
        option !== customEntryOption && option === candidate.vehicleType,
    );
    setValue("vehicleType", knownVehicleType ?? customEntryOption, {
      shouldDirty: true,
      shouldValidate: true,
    });
    setValue(
      "vehicleTypeOther",
      knownVehicleType ? "" : candidate.vehicleType,
      {
        shouldDirty: true,
        shouldValidate: true,
      },
    );
  }

  function selectEventAuthorization(event: EventAuthorization | null) {
    clearErrors("eventAuthorizationId");
    setValue("eventAuthorizationId", event ? String(event.id) : "", {
      shouldDirty: true,
      shouldValidate: true,
    });
  }

  async function submit(values: AccessEntryFormValues, intent: SubmitIntent) {
    setRequestError(null);
    setSuccessNotice(null);
    setPendingIntent(intent);

    try {
      const entryFields = {
        categoryName: values.categoryName,
        driverName: values.driverName,
        objective:
          values.objective === customEntryOption
            ? values.objectiveOther
            : values.objective,
        observation: values.observation || undefined,
        plate: values.plate,
        vehicleType:
          values.vehicleType === customEntryOption
            ? values.vehicleTypeOther
            : values.vehicleType || undefined,
      };
      const input: RegisterAccessEntryInput = selectedCandidate
        ? {
            ...entryFields,
            personId: selectedCandidate.personId,
            vehicleId: selectedCandidate.vehicleId,
          }
        : entryFields;
      if (values.eventAuthorizationId) {
        input.eventAuthorizationId = Number(values.eventAuthorizationId);
      }
      await registerAccessEntry(input);
      if (intent === "review") {
        navigate("/acessos/abertos", {
          state: { notice: "Entrada registrada com sucesso." },
        });
        return;
      }

      reset(defaultValues);
      setSelectedCandidate(null);
      setEventSectionOpen(false);
      setAdditionalDetailsOpen(false);
      setSuccessNotice(
        "Entrada registrada. O formulário está pronto para o próximo veículo.",
      );
      window.requestAnimationFrame(() =>
        document.getElementById("plate")?.focus(),
      );
    } catch (error) {
      const validationErrors = getApiValidationErrors(error);
      const accessRecordError = validationErrors.accessRecord;
      let hasFieldError = false;

      for (const [apiField, message] of Object.entries(validationErrors)) {
        if (apiField === "accessRecord") continue;
        const formField =
          apiField === "objective"
            ? values.objective === customEntryOption
              ? "objectiveOther"
              : "objective"
            : apiField === "vehicleType"
              ? values.vehicleType === customEntryOption
                ? "vehicleTypeOther"
                : "vehicleType"
              : fieldNames[apiField];
        if (!formField) continue;
        if (formField === "observation") setAdditionalDetailsOpen(true);
        setError(formField, { message, type: "server" });
        hasFieldError = true;
      }

      const description = describeApiError(error);
      setRequestError(
        accessRecordError ??
          (hasFieldError
            ? "Revise os campos destacados e tente novamente."
            : description.message),
      );
    } finally {
      setPendingIntent(null);
    }
  }

  function handleInvalidSubmission() {
    setRequestError(null);
    setSuccessNotice(null);
  }

  const submitAndContinue = handleSubmit(
    (values) => submit(values, "continue"),
    handleInvalidSubmission,
  );
  const submitAndReview = handleSubmit(
    (values) => submit(values, "review"),
    handleInvalidSubmission,
  );

  return (
    <div>
      <PageHeader
        description="Registre o veículo, o condutor e a finalidade do acesso geral. O horário oficial é definido pelo servidor."
        eyebrow="Operação da portaria"
        title="Registrar entrada"
      />

      {successNotice && (
        <div
          className="mt-6 rounded-2xl border border-brand/30 bg-brand/10 p-4 text-sm font-semibold text-brand-dark"
          role="status"
        >
          {successNotice}
        </div>
      )}

      {requestError && (
        <div
          className="mt-6 rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900"
          role="alert"
        >
          {requestError}
        </div>
      )}

      <form
        className="mt-8 grid gap-6 xl:grid-cols-[minmax(0,1fr)_21rem]"
        noValidate
        onSubmit={submitAndContinue}
      >
        <section className="overflow-hidden rounded-[2rem] border border-ink/10 bg-white shadow-[0_14px_40px_rgba(1,36,40,0.06)]">
          <div className="border-b border-ink/8 bg-[#B8C9A4]/25 px-5 py-5 sm:px-7">
            <p className="text-xs font-bold uppercase tracking-[0.14em] text-brand-dark">
              Fluxo geral de veículos
            </p>
            <h2 className="mt-3 font-display text-2xl text-ink">
              Dados da entrada
            </h2>
          </div>

          <div className="space-y-7 p-5 sm:p-7">
            <EntryCandidateSearch
              onClear={useManualEntry}
              onSelect={selectCandidate}
              selectedCandidate={selectedCandidate}
            />

            <div className="grid gap-5 md:grid-cols-2">
              <div>
                <label
                  className="text-sm font-semibold text-ink"
                  htmlFor="plate"
                >
                  Placa do veículo <span className="text-red-700">*</span>
                </label>
                <input
                  aria-describedby={errors.plate ? "plate-error" : undefined}
                  aria-invalid={Boolean(errors.plate)}
                  autoCapitalize="characters"
                  className={fieldClass}
                  id="plate"
                  maxLength={10}
                  placeholder="Ex.: DEM-1A23"
                  {...register("plate", {
                    onChange: () => clearSelectedCandidateAfterEdit("plate"),
                  })}
                />
                <FieldError id="plate-error" message={errors.plate?.message} />
              </div>

              <div>
                <label
                  className="text-sm font-semibold text-ink"
                  htmlFor="driverName"
                >
                  Nome do condutor <span className="text-red-700">*</span>
                </label>
                <input
                  aria-describedby={
                    errors.driverName ? "driverName-error" : undefined
                  }
                  aria-invalid={Boolean(errors.driverName)}
                  autoComplete="off"
                  className={fieldClass}
                  id="driverName"
                  maxLength={200}
                  placeholder="Ex.: Pessoa de demonstração"
                  {...register("driverName", {
                    onChange: () =>
                      clearSelectedCandidateAfterEdit("driverName"),
                  })}
                />
                <FieldError
                  id="driverName-error"
                  message={errors.driverName?.message}
                />
              </div>

              <div>
                <label
                  className="text-sm font-semibold text-ink"
                  htmlFor="categoryName"
                >
                  Categoria do acesso <span className="text-red-700">*</span>
                </label>
                <Controller
                  control={control}
                  name="categoryName"
                  render={({ field }) => (
                    <SelectField
                      aria-describedby={
                        errors.categoryName ? "categoryName-error" : undefined
                      }
                      aria-invalid={Boolean(errors.categoryName)}
                      className={fieldClass}
                      id="categoryName"
                      name={field.name}
                      onBlur={field.onBlur}
                      onValueChange={field.onChange}
                      options={generalAccessCategories.map((option) => ({
                        label: option,
                        value: option,
                      }))}
                      required
                      value={field.value}
                    />
                  )}
                />
                <FieldError
                  id="categoryName-error"
                  message={errors.categoryName?.message}
                />
              </div>

              <EntryVehicleTypeField
                clearErrors={clearErrors}
                control={control}
                errors={errors}
                register={register}
                selectedVehicleType={selectedVehicleType}
                setFocus={setFocus}
              />

              <Controller
                control={control}
                name="objective"
                render={({ field }) => (
                  <EntryObjectiveFieldset
                    clearErrors={clearErrors}
                    errors={errors}
                    fieldRef={field.ref}
                    onBlur={field.onBlur}
                    onSelect={field.onChange}
                    register={register}
                    selectedObjective={field.value}
                  />
                )}
              />

              <EntryAdditionalDetails
                errors={errors}
                onOpenChange={setAdditionalDetailsOpen}
                open={additionalDetailsOpen}
                register={register}
              />
            </div>

            <section
              className="rounded-3xl border border-[#AFCBE3] bg-[#BDD8F1]/20 p-4 sm:p-5"
              aria-labelledby="event-link-title"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h3
                      className="font-display text-xl text-ink"
                      id="event-link-title"
                    >
                      Autorização de evento
                    </h3>
                    <span className="rounded-full border border-ink/15 bg-white/75 px-2.5 py-1 text-xs font-bold text-ink-soft">
                      Opcional
                    </span>
                  </div>
                  <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-soft">
                    Escolha uma autorização vigente ou mantenha a entrada sem
                    vínculo.
                  </p>
                  {selectedEvent && !eventSectionOpen && (
                    <p className="mt-2 text-sm font-bold text-brand-dark">
                      Evento vinculado: {selectedEvent.name}
                    </p>
                  )}
                </div>
                <button
                  aria-label={
                    eventSectionOpen
                      ? "Ocultar autorizações de evento"
                      : selectedEvent
                        ? "Alterar autorização vinculada"
                        : "Vincular uma autorização de evento"
                  }
                  aria-controls="event-authorization-options"
                  aria-expanded={eventSectionOpen}
                  className="min-h-11 rounded-xl border border-brand-dark bg-white/60 px-4 text-sm font-bold text-brand-dark hover:bg-white focus:outline-none focus-visible:ring-3 focus-visible:ring-brand/25"
                  onClick={() => setEventSectionOpen((current) => !current)}
                  type="button"
                >
                  {eventSectionOpen
                    ? "Ocultar"
                    : selectedEvent
                      ? "Alterar"
                      : "Vincular a evento"}
                </button>
              </div>

              {eventSectionOpen && (
                <div className="mt-5" id="event-authorization-options">
                  <EventAuthorizationSelector
                    errorMessage={eventAuthorizations.errorMessage}
                    events={eventAuthorizations.events}
                    onRetry={() => void eventAuthorizations.retry()}
                    onSelect={selectEventAuthorization}
                    selectedId={
                      selectedEventAuthorizationId
                        ? Number(selectedEventAuthorizationId)
                        : null
                    }
                    selectionError={errors.eventAuthorizationId?.message}
                    status={eventAuthorizations.status}
                  />
                </div>
              )}
            </section>

            <div className="flex flex-col gap-3 border-t border-ink/10 pt-6 sm:flex-row sm:flex-wrap sm:justify-end">
              <button
                className="min-h-12 rounded-xl border border-ink/20 px-5 font-bold text-ink hover:bg-cream focus:outline-none focus-visible:ring-3 focus-visible:ring-brand/25"
                disabled={isSubmitting}
                onClick={() => navigate("/visao-geral")}
                type="button"
              >
                Cancelar
              </button>
              <button
                className="min-h-12 rounded-xl bg-brand-dark px-7 font-bold text-white shadow-sm hover:bg-ink focus:outline-none focus-visible:ring-3 focus-visible:ring-ink focus-visible:ring-offset-2 focus-visible:ring-offset-white disabled:cursor-wait disabled:bg-brand-soft disabled:text-ink disabled:opacity-100"
                disabled={isSubmitting}
                type="submit"
              >
                {isSubmitting && pendingIntent === "continue"
                  ? "Registrando…"
                  : "Registrar e continuar"}
              </button>
              <button
                className="min-h-12 rounded-xl border border-brand-dark px-5 font-bold text-brand-dark hover:bg-brand/10 focus:outline-none focus-visible:ring-3 focus-visible:ring-brand/25 disabled:cursor-wait disabled:opacity-65"
                disabled={isSubmitting}
                onClick={() => void submitAndReview()}
                type="button"
              >
                {isSubmitting && pendingIntent === "review"
                  ? "Registrando…"
                  : "Registrar e ver acessos"}
              </button>
            </div>
          </div>
        </section>

        <aside className="h-fit space-y-4 xl:sticky xl:top-8">
          <section className="rounded-[2rem] bg-[#B8C9A4] p-6 text-ink">
            <p className="text-xs font-bold uppercase tracking-[0.14em]">
              Conferência rápida
            </p>
            <ol className="mt-5 space-y-4 text-sm leading-6">
              <li className="flex gap-3">
                <strong>1.</strong>
                <span>Confirme a placa com o veículo.</span>
              </li>
              <li className="flex gap-3">
                <strong>2.</strong>
                <span>Confirme o nome do condutor.</span>
              </li>
              <li className="flex gap-3">
                <strong>3.</strong>
                <span>Registre apenas os dados necessários.</span>
              </li>
            </ol>
          </section>

          <section className="rounded-[2rem] border border-[#BDD8F1] bg-[#BDD8F1]/30 p-5">
            <div className="flex items-start gap-3">
              <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-white text-ink">
                <Icon name="bus" size={20} />
              </span>
              <div>
                <p className="text-sm font-bold text-ink">
                  Veículo institucional
                </p>
                <p className="mt-1 text-xs leading-5 text-ink-soft">
                  Saída, quilometragem, motorista e retorno são registrados em
                  <Link
                    className="ml-1 inline rounded font-bold text-ink underline decoration-ink/45 underline-offset-2 hover:decoration-ink focus:outline-none focus-visible:ring-2 focus-visible:ring-ink"
                    to="/utilizacoes-institucionais"
                  >
                    Utilizações da frota
                  </Link>
                  .
                </p>
              </div>
            </div>
          </section>
        </aside>
      </form>
    </div>
  );
}
