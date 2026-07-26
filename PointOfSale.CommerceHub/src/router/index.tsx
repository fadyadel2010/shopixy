import { createBrowserRouter } from "react-router-dom";

import { AppLayout } from "@/layouts/AppLayout";
import { CartPage } from "@/pages/CartPage";
import { CheckoutPage } from "@/pages/CheckoutPage";
import { CustomerDashboardPage } from "@/pages/CustomerDashboardPage";
import { CustomerForgotPasswordPage } from "@/pages/CustomerForgotPasswordPage";
import { CustomerLoginPage } from "@/pages/CustomerLoginPage";
import { CustomerOrderDetailsPage } from "@/pages/CustomerOrderDetailsPage";
import { CustomerOrdersPage } from "@/pages/CustomerOrdersPage";
import { CustomerRefundsPage } from "@/pages/CustomerRefundsPage";
import { CustomerRegisterPage } from "@/pages/CustomerRegisterPage";
import { CustomerResetPasswordPage } from "@/pages/CustomerResetPasswordPage";
import { CustomerSettingsPage } from "@/pages/CustomerSettingsPage";
import { CustomerVerifyEmailPage } from "@/pages/CustomerVerifyEmailPage";
import { HomePage } from "@/pages/HomePage";
import { OrderPaymentRecoveryPage } from "@/pages/OrderPaymentRecoveryPage";
import { MarketplaceContentPage } from "@/pages/MarketplaceContentPage";
import { ProductDetailsPage } from "@/pages/ProductDetailsPage";
import { ProductsPage } from "@/pages/ProductsPage";
import {
  RedirectAuthenticatedCustomers,
  RequireCustomerAuth,
  RequireCustomerNavigationCapability
} from "@/router/CustomerRouteGuards";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppLayout />,
    children: [
      {
        index: true,
        element: <HomePage />
      },
      {
        path: "products",
        element: <ProductsPage />
      },
      {
        path: "product/:id",
        element: <ProductDetailsPage />
      },
      {
        path: "cart",
        element: <CartPage />
      },
      {
        path: "checkout",
        element: <CheckoutPage />
      },
      {
        path: "orders/:id/recover",
        element: <OrderPaymentRecoveryPage />
      },
      {
        path: "verify-email",
        element: <CustomerVerifyEmailPage />
      },
      {
        element: <RedirectAuthenticatedCustomers />,
        children: [
          {
            path: "login",
            element: <CustomerLoginPage />
          },
          {
            path: "register",
            element: <CustomerRegisterPage />
          },
          {
            path: "forgot-password",
            element: <CustomerForgotPasswordPage />
          },
          {
            path: "reset-password",
            element: <CustomerResetPasswordPage />
          }
        ]
      },
      {
        element: <RequireCustomerAuth />,
        children: [
          {
            element: <RequireCustomerNavigationCapability capability="customer.account.view" />,
            children: [
              {
                path: "customer",
                element: <CustomerDashboardPage />
              }
            ]
          },
          {
            element: <RequireCustomerNavigationCapability capability="customer.account.settings" />,
            children: [
              {
                path: "customer/settings",
                element: <CustomerSettingsPage />
              }
            ]
          },
          {
            element: <RequireCustomerNavigationCapability capability="customer.orders.view" />,
            children: [
              {
                path: "orders",
                element: <CustomerOrdersPage />
              },
              {
                path: "orders/:id",
                element: <CustomerOrderDetailsPage />
              }
            ]
          },
          {
            element: <RequireCustomerNavigationCapability capability="customer.refunds.view" />,
            children: [
              {
                path: "customer/refunds",
                element: <CustomerRefundsPage />
              }
            ]
          }
        ]
      },
      {
        path: ":slug",
        element: <MarketplaceContentPage />
      }
    ]
  }
]);
