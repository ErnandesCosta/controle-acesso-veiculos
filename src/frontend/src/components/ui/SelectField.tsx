import * as SelectPrimitive from "@radix-ui/react-select";

import { Icon } from "./Icon";

const emptyValue = "__empty-selection__";

export interface SelectOption {
  disabled?: boolean;
  label: string;
  value: string;
}

interface SelectFieldProps {
  "aria-describedby"?: string;
  "aria-invalid"?: boolean;
  className?: string;
  disabled?: boolean;
  id: string;
  name?: string;
  onBlur?: () => void;
  onCloseAutoFocus?: (event: Event) => void;
  onValueChange: (value: string) => void;
  options: SelectOption[];
  placeholder?: string;
  required?: boolean;
  value: string;
}

function fromRadixValue(value: string) {
  return value === emptyValue ? "" : value;
}

function toRadixValue(value: string) {
  return value === "" ? emptyValue : value;
}

export function SelectField({
  "aria-describedby": describedBy,
  "aria-invalid": invalid,
  className = "",
  disabled,
  id,
  name,
  onBlur,
  onCloseAutoFocus,
  onValueChange,
  options,
  placeholder,
  required,
  value,
}: SelectFieldProps) {
  return (
    <SelectPrimitive.Root
      disabled={disabled}
      name={name}
      onValueChange={(nextValue) => onValueChange(fromRadixValue(nextValue))}
      required={required}
      value={toRadixValue(value)}
    >
      <SelectPrimitive.Trigger
        aria-describedby={describedBy}
        aria-invalid={invalid}
        className={`inline-flex items-center justify-between gap-3 text-left ${className}`}
        data-value={value}
        id={id}
        onBlur={onBlur}
      >
        <SelectPrimitive.Value placeholder={placeholder} />
        <SelectPrimitive.Icon className="shrink-0 text-ink" asChild>
          <Icon name="chevron-down" size={19} />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>

      <SelectPrimitive.Portal>
        <SelectPrimitive.Content
          className="z-50 max-h-[min(22rem,var(--radix-select-content-available-height))] min-w-[var(--radix-select-trigger-width)] overflow-hidden rounded-2xl border border-ink/15 bg-white p-1.5 text-ink shadow-[0_18px_45px_rgba(0,73,83,0.18)]"
          collisionPadding={12}
          onCloseAutoFocus={onCloseAutoFocus}
          position="popper"
          sideOffset={6}
        >
          <SelectPrimitive.ScrollUpButton className="flex h-8 items-center justify-center text-ink-soft">
            <Icon className="rotate-180" name="chevron-down" size={18} />
          </SelectPrimitive.ScrollUpButton>
          <SelectPrimitive.Viewport>
            {options.map((option) => (
              <SelectPrimitive.Item
                className="relative flex min-h-11 cursor-default select-none items-center rounded-xl border-b border-ink/10 py-2.5 pl-4 pr-10 text-sm outline-none last:border-b-0 data-[disabled]:pointer-events-none data-[disabled]:opacity-45 data-[highlighted]:bg-brand-soft/30 data-[highlighted]:text-ink data-[state=checked]:border-transparent data-[state=checked]:bg-brand-soft/55 data-[state=checked]:font-bold data-[state=checked]:text-ink"
                data-value={option.value}
                disabled={option.disabled}
                key={`${option.value}-${option.label}`}
                value={toRadixValue(option.value)}
              >
                <SelectPrimitive.ItemText>
                  {option.label}
                </SelectPrimitive.ItemText>
                <SelectPrimitive.ItemIndicator
                  className="absolute right-3 font-bold"
                  aria-hidden="true"
                >
                  ✓
                </SelectPrimitive.ItemIndicator>
              </SelectPrimitive.Item>
            ))}
          </SelectPrimitive.Viewport>
          <SelectPrimitive.ScrollDownButton className="flex h-8 items-center justify-center text-ink-soft">
            <Icon name="chevron-down" size={18} />
          </SelectPrimitive.ScrollDownButton>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  );
}
