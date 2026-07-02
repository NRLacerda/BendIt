import * as Dialog from "@radix-ui/react-dialog";
import * as Select from "@radix-ui/react-select";
import * as Switch from "@radix-ui/react-switch";
import { Check, ChevronDown, X } from "lucide-react";
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, TextareaHTMLAttributes } from "react";

export function Button({ className = "", ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button className={className} {...props} />;
}

export function Field(props: { label: string; children: ReactNode; className?: string }) {
  return (
    <label className={props.className}>
      <span>{props.label}</span>
      {props.children}
    </label>
  );
}

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} />;
}

export function TextArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} />;
}

export function SelectField<T extends string>(props: {
  value: T;
  onChange: (value: T) => void;
  options: Array<{ value: T; label: string }>;
  label: string;
}) {
  return (
    <Select.Root value={props.value} onValueChange={(value) => props.onChange(value as T)}>
      <Select.Trigger className="select-trigger" aria-label={props.label}>
        <Select.Value />
        <Select.Icon><ChevronDown size={16} /></Select.Icon>
      </Select.Trigger>
      <Select.Portal>
        <Select.Content className="select-content" position="popper">
          <Select.Viewport>
            {props.options.map((option) => (
              <Select.Item className="select-item" value={option.value} key={option.value}>
                <Select.ItemText>{option.label}</Select.ItemText>
                <Select.ItemIndicator><Check size={14} /></Select.ItemIndicator>
              </Select.Item>
            ))}
          </Select.Viewport>
        </Select.Content>
      </Select.Portal>
    </Select.Root>
  );
}

export function Toggle(props: { checked: boolean; onCheckedChange: (checked: boolean) => void; label: string }) {
  return (
    <label className="switch-row">
      <Switch.Root className="switch-root" checked={props.checked} onCheckedChange={props.onCheckedChange}>
        <Switch.Thumb className="switch-thumb" />
      </Switch.Root>
      <span>{props.label}</span>
    </label>
  );
}

export function ConfirmDialog(props: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
}) {
  return (
    <Dialog.Root open={props.open} onOpenChange={props.onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content className="dialog-content">
          <div className="dialog-title-row">
            <Dialog.Title>Confirm run</Dialog.Title>
            <Dialog.Close className="dialog-close" aria-label="Close"><X size={18} /></Dialog.Close>
          </div>
          <Dialog.Description>
            The requests executed by this test set can harm or change data in your application.
            These tests SHOULD be executed only in a controlled environment.
          </Dialog.Description>
          <div className="dialog-actions">
            <Dialog.Close>Cancel</Dialog.Close>
            <Button className="danger-button" onClick={props.onConfirm}>Proceed</Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
