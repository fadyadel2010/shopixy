import { FormEvent, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";

import { normalizeApiError } from "@/api";
import { broadcastCustomerLogout } from "@/auth/customer-session-authority";
import { customerQueryKeys } from "@/features/customer/queries";
import { customerAuthService } from "@/services";
import { selectCustomerProfile, useCustomerAuthStore } from "@/store";
import { Button } from "@/ui/button";
import { Card, CardContent } from "@/ui/card";
import { Input } from "@/ui/input";
import { useDocumentTitle } from "@/utils";

export const CustomerSettingsPage = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const customer = useCustomerAuthStore(selectCustomerProfile);
  const clearSession = useCustomerAuthStore((state) => state.clearSession);
  const hydrateSession = useCustomerAuthStore((state) => state.hydrateSession);
  const [name, setName] = useState(customer?.name ?? "");
  const [phoneNumber, setPhoneNumber] = useState(customer?.phoneNumber ?? "");
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newEmail, setNewEmail] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useDocumentTitle("Account Settings | CommerceHub");

  const run = async (action: () => Promise<{ message?: string } | void>, signOut = false) => {
    setError(null);
    setMessage(null);
    try {
      const response = await action();
      if (signOut) {
        clearSession();
        broadcastCustomerLogout();
        queryClient.removeQueries({
          queryKey: customerQueryKeys.all
        });
        navigate("/login", { replace: true });
        return;
      }
      setMessage(response?.message ?? "Account settings updated.");
    } catch (requestError) {
      setError(normalizeApiError(requestError).message);
    }
  };

  const saveProfile = async (event: FormEvent) => {
    event.preventDefault();
    await run(async () => {
      await customerAuthService.updateProfile({ name: name.trim(), phoneNumber: phoneNumber.trim() || null });
      await hydrateSession();
    });
  };

  return (
    <div className="mx-auto max-w-3xl space-y-5">
      <h1 className="font-display text-4xl font-black tracking-tight text-foreground">Account settings</h1>
      {message ? <p className="rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{message}</p> : null}
      {error ? <p className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</p> : null}

      <Card><CardContent className="space-y-4 p-6">
        <h2 className="text-xl font-black">Profile</h2>
        <form className="space-y-3" onSubmit={saveProfile}>
          <Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Full name" />
          <Input value={phoneNumber} onChange={(event) => setPhoneNumber(event.target.value)} placeholder="Phone number" />
          <Button type="submit" disabled={!name.trim()}>Save profile</Button>
        </form>
      </CardContent></Card>

      <Card><CardContent className="space-y-4 p-6">
        <h2 className="text-xl font-black">Email verification</h2>
        <p className="text-sm text-muted-foreground">{customer?.isEmailVerified ? "Your email is verified." : "Verify your email to keep account recovery reliable."}</p>
        {!customer?.isEmailVerified && customer?.email ? <Button variant="outline" onClick={() => void run(() => customerAuthService.resendVerification({ email: customer.email }))}>Resend verification email</Button> : null}
      </CardContent></Card>

      <Card><CardContent className="space-y-4 p-6">
        <h2 className="text-xl font-black">Change credentials</h2>
        <Input type="password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} placeholder="Current password" />
        <Input type="password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} placeholder="New password" />
        <Button variant="outline" disabled={!currentPassword || newPassword.length < 8} onClick={() => void run(() => customerAuthService.changePassword({ currentPassword, newPassword }), true)}>Change password</Button>
        <Input type="email" value={newEmail} onChange={(event) => setNewEmail(event.target.value)} placeholder="New email" />
        <Button variant="outline" disabled={!currentPassword || !newEmail.trim()} onClick={() => void run(() => customerAuthService.changeEmail({ currentPassword, newEmail: newEmail.trim() }), true)}>Change email</Button>
      </CardContent></Card>

      <Card><CardContent className="space-y-4 p-6">
        <h2 className="text-xl font-black">Sessions</h2>
        <p className="text-sm text-muted-foreground">Revoke every active browser session, including this one.</p>
        <Button variant="outline" onClick={() => void run(() => customerAuthService.revokeAllSessions(), true)}>Revoke all sessions</Button>
      </CardContent></Card>
    </div>
  );
};
