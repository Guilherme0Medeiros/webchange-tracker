"use client";

import { useState, type ChangeEventHandler } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Input } from "@/components/ui/input";

type PasswordFieldProps = {
  id: string;
  label: string;
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
  autoComplete: "current-password" | "new-password";
  describedBy?: string;
  visibilityLabel?: string;
};

export function PasswordField({ id, label, value, onChange, autoComplete, describedBy,
  visibilityLabel = label.toLowerCase() }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);
  return <div>
    <label className="block text-sm font-medium" htmlFor={id}>{label}</label>
    <div className="relative mt-2">
      <Input id={id} type={visible ? "text" : "password"} autoComplete={autoComplete}
        aria-describedby={describedBy} required value={value} onChange={onChange} className="pr-12" />
      <button type="button" onClick={() => setVisible(current => !current)}
        aria-label={`${visible ? "Ocultar" : "Mostrar"} ${visibilityLabel}`}
        aria-pressed={visible}
        className="absolute inset-y-0 right-2 my-auto flex h-8 w-8 items-center justify-center rounded-md text-slate-500 hover:text-slate-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-slate-500">
        {visible ? <EyeOff aria-hidden="true" className="h-4 w-4" /> : <Eye aria-hidden="true" className="h-4 w-4" />}
      </button>
    </div>
  </div>;
}
