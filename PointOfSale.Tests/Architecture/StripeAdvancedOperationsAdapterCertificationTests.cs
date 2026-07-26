using System.Net;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Payments.Runtime.Domain;
using Stripe;
using StripeCharge = Stripe.Charge;
using StripePaymentIntent = Stripe.PaymentIntent;
using StripeRefund = Stripe.Refund;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeAdvancedOperationsAdapterCertificationTests
{
    [Fact]
    public async Task CaptureMapping_MustTranslateCanonicalActionToStripeCaptureAndProviderEvidence()
    {
        var action = CreateAction(StripeAdvancedOperation.Capture, new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.Amount] = "12.34",
            [StripeOperationMetadataKeys.Currency] = "USD"
        });
        var client = new CapturingStripeAdvancedOperationsClient();
        var adapter = new StripeAdvancedOperationsAdapter(client);

        var result = await adapter.ExecuteAsync(action);

        Assert.True(result.Succeeded);
        Assert.Equal("pi_operation", client.CapturedPaymentIntentId);
        Assert.Equal(1234, client.CaptureOptions!.AmountToCapture);
        Assert.Equal("Capture", client.CaptureOptions.Metadata["operation"]);
        Assert.StartsWith("stripe:op:Capture:", client.CaptureRequestOptions!.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal(EvidenceType.ProviderNotification, result.Evidence!.Type);
        Assert.Equal("pi_operation", result.Evidence.Reference.Value);
        Assert.Equal("Capture", result.Evidence.Metadata.Values["operation"]);
        Assert.Equal("succeeded", result.Evidence.Metadata.Values["operation_status"]);
    }

    [Fact]
    public async Task RefundMapping_MustTranslateCanonicalActionToStripeRefundAndProviderEvidence()
    {
        var action = CreateAction(StripeAdvancedOperation.Refund, new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.Amount] = "20.00",
            [StripeOperationMetadataKeys.Currency] = "EGP",
            [StripeOperationMetadataKeys.Reason] = "requested_by_customer"
        });
        var client = new CapturingStripeAdvancedOperationsClient();
        var adapter = new StripeAdvancedOperationsAdapter(client);

        var result = await adapter.ExecuteAsync(action);

        Assert.True(result.Succeeded);
        Assert.Equal("pi_operation", client.RefundOptions!.PaymentIntent);
        Assert.Equal(2000, client.RefundOptions.Amount);
        Assert.Equal("egp", client.RefundOptions.Currency);
        Assert.Equal("requested_by_customer", client.RefundOptions.Reason);
        Assert.Equal("Refund", client.RefundOptions.Metadata["operation"]);
        Assert.Equal("re_certified", result.Evidence!.Reference.Value);
        Assert.Equal("succeeded", result.Evidence.Metadata.Values["operation_status"]);
        Assert.Equal("re_certified", result.Evidence.Metadata.Values["refund_reference"]);
    }

    [Fact]
    public async Task PartialRefundMapping_MustUseStripeRefundWithAmountAndDeterministicIdempotency()
    {
        var first = CreateAction(StripeAdvancedOperation.PartialRefund, new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.Amount] = "5.50",
            [StripeOperationMetadataKeys.Currency] = "EGP"
        });
        var second = CreateAction(StripeAdvancedOperation.PartialRefund, new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.Currency] = "EGP",
            [StripeOperationMetadataKeys.Amount] = "5.50"
        });
        var firstRequest = StripeOperationFactory.Create(first);
        var secondRequest = StripeOperationFactory.Create(second);

        Assert.Equal(firstRequest.IdempotencyKey, secondRequest.IdempotencyKey);
        Assert.Equal(550, firstRequest.RefundOptions!.Amount);
        Assert.StartsWith("stripe:op:PartialRefund:", firstRequest.IdempotencyKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationValidation_MustFailClosedForMissingPartialAmountInvalidReferenceAndOversizedIdempotency()
    {
        var adapter = new StripeAdvancedOperationsAdapter(new CapturingStripeAdvancedOperationsClient());
        var missingPartialAmount = await adapter.ExecuteAsync(CreateAction(
            StripeAdvancedOperation.PartialRefund,
            new Dictionary<string, string> { [StripeOperationMetadataKeys.Currency] = "EGP" }));
        var invalidReference = await adapter.ExecuteAsync(CreateAction(
            StripeAdvancedOperation.Capture,
            providerReference: "ch_wrong_for_capture"));
        var oversizedKeyAction = CreateAction(
            StripeAdvancedOperation.Capture,
            new Dictionary<string, string>
            {
                [StripeOperationMetadataKeys.IdempotencyKey] = new string('k', 400),
                [StripeOperationMetadataKeys.OperationReference] = "capture-operation-42"
            });
        var oversizedKeyRequest = StripeOperationFactory.Create(oversizedKeyAction);

        Assert.False(missingPartialAmount.Succeeded);
        Assert.Equal("STRIPE_OPERATION_REQUEST_INVALID", missingPartialAmount.Error!.Code);
        Assert.False(invalidReference.Succeeded);
        Assert.Equal("STRIPE_OPERATION_REQUEST_INVALID", invalidReference.Error!.Code);
        Assert.Equal(255, oversizedKeyRequest.IdempotencyKey.Value.Length);
        Assert.Equal("capture-operation-42", oversizedKeyRequest.OperationReference.Value);
    }

    [Fact]
    public async Task CancelMapping_MustTranslateCanonicalActionToStripeCancel()
    {
        var action = CreateAction(StripeAdvancedOperation.Cancel, new Dictionary<string, string>
        {
            [StripeOperationMetadataKeys.Reason] = "abandoned"
        });
        var client = new CapturingStripeAdvancedOperationsClient();
        var adapter = new StripeAdvancedOperationsAdapter(client);

        var result = await adapter.ExecuteAsync(action);

        Assert.True(result.Succeeded);
        Assert.Equal("pi_operation", client.CanceledPaymentIntentId);
        Assert.Equal("abandoned", client.CancelOptions!.CancellationReason);
        Assert.Equal("canceled", result.Evidence!.Metadata.Values["operation_status"]);
        Assert.Equal("Cancel", result.Evidence.Metadata.Values["operation"]);
    }

    [Fact]
    public async Task RetrieveOperations_MustReturnProviderEvidenceOnly()
    {
        var paymentIntentAction = CreateAction(StripeAdvancedOperation.RetrievePaymentIntent);
        var chargeAction = CreateAction(
            StripeAdvancedOperation.RetrieveCharge,
            providerReference: "ch_operation");
        var client = new CapturingStripeAdvancedOperationsClient();
        var adapter = new StripeAdvancedOperationsAdapter(client);

        var paymentIntentResult = await adapter.ExecuteAsync(paymentIntentAction);
        var chargeResult = await adapter.ExecuteAsync(chargeAction);

        Assert.True(paymentIntentResult.Succeeded);
        Assert.True(chargeResult.Succeeded);
        Assert.Equal("pi_operation", client.RetrievedPaymentIntentId);
        Assert.Equal("ch_operation", client.RetrievedChargeId);
        Assert.IsType<ProviderEvidence>(paymentIntentResult.Evidence);
        Assert.IsType<ProviderEvidence>(chargeResult.Evidence);
        Assert.Equal("RetrievePaymentIntent", paymentIntentResult.Evidence!.Metadata.Values["operation"]);
        Assert.Equal("RetrieveCharge", chargeResult.Evidence!.Metadata.Values["operation"]);
    }

    [Theory]
    [InlineData("charge_already_captured", CanonicalProviderErrorType.AlreadyCaptured)]
    [InlineData("charge_already_refunded", CanonicalProviderErrorType.AlreadyRefunded)]
    [InlineData("payment_intent_unexpected_state", CanonicalProviderErrorType.InvalidState)]
    public async Task ErrorTranslation_MustReturnCanonicalProviderErrorsWithoutStripeExceptionLeak(
        string stripeCode,
        CanonicalProviderErrorType expectedType)
    {
        var adapter = new StripeAdvancedOperationsAdapter(
            new ThrowingStripeAdvancedOperationsClient(stripeCode));

        var result = await adapter.ExecuteAsync(CreateAction(StripeAdvancedOperation.Capture));

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.Equal(expectedType, result.Error!.Type);
        Assert.Equal(stripeCode, result.Error.Code);
        Assert.Equal("stripe", result.Error.Metadata["provider"]);
    }

    [Fact]
    public async Task Serialization_MustRoundTripProviderEvidenceWithoutStripeTypes()
    {
        var adapter = new StripeAdvancedOperationsAdapter(new CapturingStripeAdvancedOperationsClient());

        var result = await adapter.ExecuteAsync(CreateAction(StripeAdvancedOperation.Capture));
        var json = JsonSerializer.Serialize(result.Evidence);
        var roundTrip = JsonSerializer.Deserialize<ProviderEvidence>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(result.Evidence!.Id, roundTrip.Id);
        Assert.DoesNotContain("PaymentIntentCaptureOptions", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe.PaymentIntent", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustKeepAdvancedAdapterOutsideRuntimeAndEvidenceOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeAdvancedOperationsAdapter.cs");

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = System.IO.File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("using Stripe", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Stripe.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CaptureAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RefundService", source, StringComparison.Ordinal);
        }

        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction(
        StripeAdvancedOperation operation,
        IReadOnlyDictionary<string, string>? extraMetadata = null,
        string providerReference = "pi_operation")
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StripeOperationMetadataKeys.Operation] = operation.ToString(),
            [StripeOperationMetadataKeys.ProviderReference] = providerReference,
            ["OrderReference"] = "order-advanced"
        };

        foreach (var (key, value) in extraMetadata ?? new Dictionary<string, string>())
        {
            values[key] = value;
        }

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.StatusRefresh,
            PaymentActionStatus.Completed,
            new ActionReference("advanced-operation"),
            new ActionCorrelationId("advanced-correlation"),
            DateTimeOffset.Parse("2026-07-12T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-12T10:02:00Z"),
            metadata: new PaymentActionMetadata(values));
    }

    private static StripePaymentIntent PaymentIntent(string status = "succeeded")
    {
        return new StripePaymentIntent
        {
            Id = "pi_operation",
            Status = status,
            Amount = 1234,
            Currency = "usd",
            Created = DateTime.SpecifyKind(DateTime.Parse("2026-07-12T10:03:00Z"), DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>
            {
                ["correlation_id"] = "advanced-correlation"
            }
        };
    }

    private static StripeRefund Refund()
    {
        return new StripeRefund
        {
            Id = "re_certified",
            Status = "succeeded",
            Amount = 2000,
            Currency = "egp",
            ChargeId = "ch_operation",
            PaymentIntentId = "pi_operation",
            Created = DateTime.SpecifyKind(DateTime.Parse("2026-07-12T10:04:00Z"), DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>()
        };
    }

    private static StripeCharge Charge()
    {
        return new StripeCharge
        {
            Id = "ch_operation",
            Status = "succeeded",
            Amount = 1234,
            Currency = "usd",
            Created = DateTime.SpecifyKind(DateTime.Parse("2026-07-12T10:05:00Z"), DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>()
        };
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return System.IO.File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private sealed class CapturingStripeAdvancedOperationsClient : IStripeAdvancedOperationsClient
    {
        public string? CapturedPaymentIntentId { get; private set; }
        public string? CanceledPaymentIntentId { get; private set; }
        public string? RetrievedPaymentIntentId { get; private set; }
        public string? RetrievedChargeId { get; private set; }
        public PaymentIntentCaptureOptions? CaptureOptions { get; private set; }
        public PaymentIntentCancelOptions? CancelOptions { get; private set; }
        public RefundCreateOptions? RefundOptions { get; private set; }
        public RequestOptions? CaptureRequestOptions { get; private set; }

        public Task<StripePaymentIntent> CapturePaymentIntentAsync(
            string paymentIntentId,
            PaymentIntentCaptureOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            CapturedPaymentIntentId = paymentIntentId;
            CaptureOptions = options;
            CaptureRequestOptions = requestOptions;
            return Task.FromResult(PaymentIntent());
        }

        public Task<StripePaymentIntent> CancelPaymentIntentAsync(
            string paymentIntentId,
            PaymentIntentCancelOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            CanceledPaymentIntentId = paymentIntentId;
            CancelOptions = options;
            return Task.FromResult(PaymentIntent("canceled"));
        }

        public Task<StripeRefund> CreateRefundAsync(
            RefundCreateOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            RefundOptions = options;
            return Task.FromResult(Refund());
        }

        public Task<StripePaymentIntent> RetrievePaymentIntentAsync(
            string paymentIntentId,
            PaymentIntentGetOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            RetrievedPaymentIntentId = paymentIntentId;
            return Task.FromResult(PaymentIntent("requires_capture"));
        }

        public Task<StripeCharge> RetrieveChargeAsync(
            string chargeId,
            ChargeGetOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            RetrievedChargeId = chargeId;
            return Task.FromResult(Charge());
        }
    }

    private sealed class ThrowingStripeAdvancedOperationsClient : IStripeAdvancedOperationsClient
    {
        private readonly string _stripeCode;

        public ThrowingStripeAdvancedOperationsClient(string stripeCode)
        {
            _stripeCode = stripeCode;
        }

        public Task<StripePaymentIntent> CapturePaymentIntentAsync(
            string paymentIntentId,
            PaymentIntentCaptureOptions options,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            throw new StripeException(
                HttpStatusCode.BadRequest,
                new StripeError
                {
                    Type = "invalid_request_error",
                    Code = _stripeCode,
                    Message = _stripeCode
                },
                _stripeCode);
        }

        public Task<StripePaymentIntent> CancelPaymentIntentAsync(string paymentIntentId, PaymentIntentCancelOptions options, RequestOptions requestOptions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StripeRefund> CreateRefundAsync(RefundCreateOptions options, RequestOptions requestOptions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StripePaymentIntent> RetrievePaymentIntentAsync(string paymentIntentId, PaymentIntentGetOptions options, RequestOptions requestOptions, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StripeCharge> RetrieveChargeAsync(string chargeId, ChargeGetOptions options, RequestOptions requestOptions, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
