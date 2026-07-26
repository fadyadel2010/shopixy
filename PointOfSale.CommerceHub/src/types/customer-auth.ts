export type CustomerProfile = {
  id: string;
  email: string;
  name: string;
  phoneNumber?: string | null;
  isEmailVerified: boolean;
  createdAt: string;
  navigationCapabilities: CustomerNavigationCapability[];
};

export type CustomerNavigationCapability =
  | "customer.account.view"
  | "customer.account.settings"
  | "customer.orders.view"
  | "customer.refunds.view";

export type CustomerRegisterRequest = {
  email: string;
  password: string;
  name: string;
  phoneNumber?: string;
};

export type CustomerLoginRequest = {
  email: string;
  password: string;
};

export type CustomerAuthResponse = {
  expiresAt: string;
  customer: CustomerProfile;
};

export type CustomerForgotPasswordRequest = {
  email: string;
};

export type CustomerResetPasswordRequest = {
  token: string;
  newPassword: string;
};

export type CustomerVerifyEmailRequest = {
  token: string;
};

export type CustomerResendVerificationRequest = {
  email: string;
};

export type CustomerUpdateProfileRequest = {
  name: string;
  phoneNumber?: string | null;
};

export type CustomerChangePasswordRequest = {
  currentPassword: string;
  newPassword: string;
};

export type CustomerChangeEmailRequest = {
  currentPassword: string;
  newEmail: string;
};
