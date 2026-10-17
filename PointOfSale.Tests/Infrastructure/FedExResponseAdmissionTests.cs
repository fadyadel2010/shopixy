using PointOfSale.Core.Enums;
using PointOfSale.Tests.Architecture;
using PointOfSale.Infrastructure.Shipping.Adapters;
using System.Text.Json.Nodes;

namespace PointOfSale.Tests.Infrastructure;

public sealed class FedExResponseAdmissionTests
{
    [Fact]
    public void ShipmentAdmission_ProducesCarrierNeutralDtoAndDoesNotExposeRawJson()
    {
        var result = FedExResponseAdmission.AdmitShipment(ValidShipmentJson());

        Assert.True(result.Success);
        Assert.NotNull(result.Dto);
        Assert.Equal("transaction-1", result.Dto.TransactionId);
        Assert.Equal("123456789012", result.Dto.ShipmentIdentifier);
        Assert.Equal("123456789012", result.Dto.TrackingNumber);
        Assert.Equal("application/pdf", result.Dto.Labels[0].MediaType);
        Assert.Equal("base64", result.Dto.Labels[0].Encoding);
        Assert.Equal("WARNING", result.Dto.Warnings[0].AlertType);
        Assert.Contains(result.Dto.ProviderReferences, reference => reference.Type == "masterTrackingNumber");
        Assert.DoesNotContain("output", result.Dto.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShipmentAdmission_RejectsMalformedJsonMissingFieldsUnexpectedFieldsAndUnknownAlertEnums()
    {
        var malformed = FedExResponseAdmission.AdmitShipment("{not-json");
        var missing = FedExResponseAdmission.AdmitShipment("""{"transactionId":"transaction-1","output":{}}""");
        var unexpected = FedExResponseAdmission.AdmitShipment(ValidShipmentJson().Replace(
            "\"transactionShipments\":",
            "\"invented\": true, \"transactionShipments\":",
            StringComparison.Ordinal));
        var unknownAlert = FedExResponseAdmission.AdmitShipment("""
            {
              "transactionId": "transaction-1",
              "output": {
                "transactionShipments": [
                  {
                    "masterTrackingNumber": "123456789012",
                    "pieceResponses": [
                      {
                        "trackingNumber": "123456789012",
                        "packageDocuments": [
                          {
                            "contentType": "LABEL",
                            "encodedLabel": "JVBERi0x"
                          }
                        ]
                      }
                    ]
                  }
                ],
                "alerts": [
                  {
                    "code": "NOTE",
                    "message": "Accepted",
                    "alertType": "MAGIC"
                  }
                ]
              }
            }
            """);

        Assert.False(malformed.Success);
        Assert.Equal(FedExResponseAdmission.InvalidJsonCode, malformed.ErrorCode);
        Assert.False(missing.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, missing.ErrorCode);
        Assert.False(unexpected.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, unexpected.ErrorCode);
        Assert.False(unknownAlert.Success);
        Assert.Equal(FedExResponseAdmission.UnknownEnumCode, unknownAlert.ErrorCode);
    }

    [Theory]
    [InlineData("""{"contentType":"LABEL","encodedLabel":"not-base64"}""")]
    [InlineData("""{"contentType":"INVOICE","encodedLabel":"JVBERi0x"}""")]
    [InlineData("""{"contentType":"LABEL","url":"ftp://example.test/label.pdf"}""")]
    [InlineData("""{"contentType":"LABEL"}""")]
    public void LabelAdmission_RejectsMalformedLabelPayloads(string labelJson)
    {
        var result = FedExResponseAdmission.AdmitLabel(labelJson);

        Assert.False(result.Success);
        Assert.True(result.ErrorCode is FedExResponseAdmission.InvalidLabelCode or FedExResponseAdmission.UnknownEnumCode);
    }

    [Fact]
    public void LabelAdmission_AcceptsOfficialLabelEvidenceOnly()
    {
        var result = FedExResponseAdmission.AdmitLabel("""
            {
              "contentType": "LABEL",
              "copiesToPrint": 1,
              "encodedLabel": "JVBERi0x",
              "url": "https://example.test/label.pdf"
            }
            """);

        Assert.True(result.Success);
        Assert.NotNull(result.Dto);
        Assert.Equal("application/pdf", result.Dto.MediaType);
        Assert.Equal("base64", result.Dto.Encoding);
        Assert.Equal("JVBERi0x", result.Dto.EncodedPayload);
        Assert.Equal("https://example.test/label.pdf", result.Dto.Url?.ToString());
    }

    [Fact]
    public void TrackingAdmission_MapsOnlyCertifiedStatusesAndOfficialEvents()
    {
        var result = FedExResponseAdmission.AdmitTracking(ValidTrackingJson());

        Assert.True(result.Success);
        Assert.NotNull(result.Dto);
        Assert.Equal(ShipmentStatus.InTransit, result.Dto.CurrentStatus);
        Assert.Equal("In transit", result.Dto.CurrentStatusDescription);
        Assert.Equal(2, result.Dto.Events.Count);
        Assert.Equal(ShipmentStatus.InTransit, result.Dto.Events[0].Status);
        Assert.Equal("IT", result.Dto.Events[0].CarrierStatusCode);
        Assert.Equal("Memphis, TN, US", result.Dto.Events[0].Location);
        Assert.Null(result.Dto.RawResponse);
    }

    [Fact]
    public void TrackingAdmission_RejectsUnknownStatusesMissingTimestampsUnexpectedStructureAndIdentifierDisagreement()
    {
        var unknownStatus = FedExResponseAdmission.AdmitTracking(ValidTrackingJson().Replace(
            "\"derivedCode\": \"IT\"",
            "\"derivedCode\": \"MAGIC\"",
            StringComparison.Ordinal));
        var missingTimestamp = FedExResponseAdmission.AdmitTracking("""
            {
              "transactionId": "transaction-1",
              "output": {
                "completeTrackResults": [
                  {
                    "trackingNumber": "123456789012",
                    "trackResults": [
                      {
                        "trackingNumberInfo": {
                          "trackingNumber": "123456789012"
                        },
                        "latestStatusDetail": {
                          "derivedCode": "IT",
                          "description": "In transit"
                        },
                        "scanEvents": [
                          {
                            "eventType": "IT",
                            "eventDescription": "In transit"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);
        var unexpected = FedExResponseAdmission.AdmitTracking(ValidTrackingJson().Replace(
            "\"latestStatusDetail\"",
            "\"rawStatus\":{},\"latestStatusDetail\"",
            StringComparison.Ordinal));
        var mismatch = FedExResponseAdmission.AdmitTracking("""
            {
              "transactionId": "transaction-1",
              "output": {
                "completeTrackResults": [
                  {
                    "trackingNumber": "999999999999",
                    "trackResults": [
                      {
                        "trackingNumberInfo": {
                          "trackingNumber": "123456789012"
                        },
                        "latestStatusDetail": {
                          "derivedCode": "IT",
                          "description": "In transit"
                        },
                        "scanEvents": [
                          {
                            "eventType": "IT",
                            "eventDescription": "In transit",
                            "date": "2026-07-08T09:15:00-05:00"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);

        Assert.False(unknownStatus.Success);
        Assert.Equal(FedExResponseAdmission.UnknownStatusCode, unknownStatus.ErrorCode);
        Assert.False(missingTimestamp.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, missingTimestamp.ErrorCode);
        Assert.False(unexpected.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, unexpected.ErrorCode);
        Assert.False(mismatch.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, mismatch.ErrorCode);
    }

    [Fact]
    public void AddressValidationAdmission_TreatsAddressValidationAsProviderEvidenceOnly()
    {
        var result = FedExResponseAdmission.AdmitAddressValidation("""
            {
              "transactionId": "transaction-1",
              "output": {
                "resolvedAddresses": [
                  {
                    "streetLinesToken": ["10 MAIN ST"],
                    "city": "MEMPHIS",
                    "stateOrProvinceCode": "TN",
                    "postalCodeToken": { "value": "38116", "changed": false },
                    "countryCode": "US",
                    "classification": "BUSINESS"
                  }
                ]
              }
            }
            """);

        Assert.True(result.Success);
        Assert.NotNull(result.Dto);
        Assert.Equal("transaction-1", result.Dto.TransactionId);
        Assert.Equal("US", result.Dto.ResolvedAddresses[0].CountryCode);
        Assert.IsType<FedExAddressValidationEvidence>(result.Dto);
    }

    [Fact]
    public void AddressValidationAdmission_RejectsUnknownClassificationAndUnexpectedSchema()
    {
        var unknownClassification = FedExResponseAdmission.AdmitAddressValidation("""
            {
              "transactionId": "transaction-1",
              "output": {
                "resolvedAddresses": [
                  {
                    "streetLinesToken": ["10 MAIN ST"],
                    "countryCode": "US",
                    "classification": "MAGIC"
                  }
                ]
              }
            }
            """);
        var unexpected = FedExResponseAdmission.AdmitAddressValidation("""
            {
              "transactionId": "transaction-1",
              "output": {
                "resolvedAddresses": [
                  {
                    "streetLinesToken": ["10 MAIN ST"],
                    "countryCode": "US",
                    "invented": true
                  }
                ]
              }
            }
            """);

        Assert.False(unknownClassification.Success);
        Assert.Equal(FedExResponseAdmission.UnknownEnumCode, unknownClassification.ErrorCode);
        Assert.False(unexpected.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, unexpected.ErrorCode);
    }

    [Fact]
    public void ServiceAvailabilityAdmission_AdmitsServicesTransitWarningsAndProviderEvidenceOnly()
    {
        var result = FedExResponseAdmission.AdmitServiceAvailability(ValidServiceAvailabilityJson());

        Assert.True(result.Success);
        Assert.NotNull(result.Dto);
        Assert.Equal("transaction-1", result.Dto.TransactionId);
        Assert.Equal(2, result.Dto.Services.Count);
        Assert.Equal("FEDEX_2_DAY", result.Dto.Services[0].ServiceType);
        Assert.Equal("FedEx 2Day", result.Dto.Services[0].ServiceName);
        Assert.Equal(2, result.Dto.Services[0].MinimumTransitDays);
        Assert.Equal(2, result.Dto.Services[0].MaximumTransitDays);
        Assert.Equal(PointOfSale.Core.DTOs.Shipping.CarrierNeutralServiceIdentifier.Express, result.Dto.Services[0].NeutralService);
        Assert.Single(result.Dto.Warnings);
        Assert.IsType<FedExServiceAvailabilityEvidence>(result.Dto);
    }

    [Fact]
    public void ServiceAvailabilityAdmission_RejectsMalformedUnknownServicesUnknownTransitUnitsAndUnexpectedSchema()
    {
        var malformed = FedExResponseAdmission.AdmitServiceAvailability("{not-json");
        var unknownService = FedExResponseAdmission.AdmitServiceAvailability(ValidServiceAvailabilityJson().Replace(
            "\"FEDEX_2_DAY\"",
            "\"MAGIC_SERVICE\"",
            StringComparison.Ordinal));
        var unknownTransitUnit = FedExResponseAdmission.AdmitServiceAvailability(ValidServiceAvailabilityJson().Replace(
            "\"TWO_DAYS\"",
            "\"HOURS\"",
            StringComparison.Ordinal));
        var unexpected = FedExResponseAdmission.AdmitServiceAvailability(ValidServiceAvailabilityJson().Replace(
            "\"transitTimes\":",
            "\"invented\": true, \"transitTimes\":",
            StringComparison.Ordinal));

        Assert.False(malformed.Success);
        Assert.Equal(FedExResponseAdmission.InvalidJsonCode, malformed.ErrorCode);
        Assert.False(unknownService.Success);
        Assert.Equal(FedExResponseAdmission.UnknownEnumCode, unknownService.ErrorCode);
        Assert.False(unknownTransitUnit.Success);
        Assert.Equal(FedExResponseAdmission.UnknownEnumCode, unknownTransitUnit.ErrorCode);
        Assert.False(unexpected.Success);
        Assert.Equal(FedExResponseAdmission.InvalidSchemaCode, unexpected.ErrorCode);
    }

    [Fact]
    public void ResponseAdmission_MustRemainOutOfAdapterTransportSqlAndShipmentMutation()
    {
        var admission = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExResponseAdmission.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var transport = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExTransport.cs");

        Assert.Contains("FedExAdmittedShipment", admission, StringComparison.Ordinal);
        Assert.Contains("ShipmentTrackingResult", admission, StringComparison.Ordinal);
        Assert.Contains("FedExServiceAvailabilityEvidence", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderStatus", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("Inventory", admission, StringComparison.Ordinal);
        Assert.DoesNotContain("RawResponse =", admission, StringComparison.Ordinal);

        Assert.Contains("FedExResponseAdmission.AdmitShipment", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitTracking", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitServiceAvailability", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("FedExResponseAdmission", transport, StringComparison.Ordinal);
    }

    private static string ValidShipmentJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "transactionShipments": [
              {
                "masterTrackingNumber": "123456789012",
                "pieceResponses": [
                  {
                    "trackingNumber": "123456789012",
                    "packageDocuments": [
                      {
                        "contentType": "LABEL",
                        "copiesToPrint": 1,
                        "encodedLabel": "JVBERi0x",
                        "url": "https://example.test/label.pdf"
                      }
                    ]
                  }
                ]
              }
            ],
            "alerts": [
              {
                "code": "NOTE",
                "message": "Accepted",
                "alertType": "WARNING"
              }
            ]
          }
        }
        """;

    [Theory]
    [InlineData("master")]
    [InlineData("piece-count")]
    [InlineData("doc-tracking")]
    [InlineData("non-pdf")]
    public void CreationAdmission_RejectsInconsistentPieceOrLabelEvidence(string defect)
    {
        var json = JsonNode.Parse(ValidShipmentJson())!;
        var shipment = json["output"]!["transactionShipments"]![0]!;
        var pieces = shipment["pieceResponses"]!.AsArray();
        var doc = pieces[0]!["packageDocuments"]![0]!;
        switch (defect)
        {
            case "master": shipment["masterTrackingNumber"] = "999999999999"; break;
            case "piece-count": pieces.Add(pieces[0]!.DeepClone()); break;
            case "doc-tracking": doc["trackingNumber"] = "999999999999"; break;
            case "non-pdf": doc["encodedLabel"] = "bm90LXBkZg=="; break;
        }
        Assert.False(FedExResponseAdmission.AdmitShipment(json.ToJsonString()).Success);
    }

    [Fact]
    public void CreationAdmission_AcceptsDocumentedResponseFieldsAndRejectsMismatchedService()
    {
        var json = JsonNode.Parse(ValidShipmentJson())!;
        json["customerTransactionId"] = "corr";
        var shipment = json["output"]!["transactionShipments"]![0]!;
        shipment["serviceType"] = "FEDEX_GROUND";
        shipment["shipDatestamp"] = "2026-10-11";
        shipment["completedShipmentDetail"] = new JsonObject();
        shipment["pieceResponses"]![0]!["packageSequenceNumber"] = 1;
        shipment["pieceResponses"]![0]!["packageDocuments"]![0]!["docType"] = "PDF";
        Assert.True(FedExResponseAdmission.AdmitShipment(json.ToJsonString(), "FEDEX_GROUND").Success);
        Assert.False(FedExResponseAdmission.AdmitShipment(json.ToJsonString(), "FEDEX_2_DAY").Success);
    }

    [Theory]
    [InlineData("2026-07-08")]
    [InlineData("2026-07-08T09:15:00")]
    [InlineData("not-time")]
    public void TrackingAdmission_DoesNotInventMissingTimeOrOffset(string date)
    {
        var json = JsonNode.Parse(ValidTrackingJson())!;
        json["output"]!["completeTrackResults"]![0]!["trackResults"]![0]!["scanEvents"]![0]!["date"] = date;
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString(), "123456789012").Success);
    }

    [Theory]
    [InlineData("DE")]
    [InlineData("SE")]
    [InlineData("RS")]
    public void TrackingAdmission_ExceptionAndReturnScansDoNotProveTerminalCompletion(string code)
    {
        var json = JsonNode.Parse(ValidTrackingJson())!;
        json["output"]!["completeTrackResults"]![0]!["trackResults"]![0]!["latestStatusDetail"]!["derivedCode"] = code;
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
    }

    [Fact]
    public void TrackingAdmission_UsesDocumentedDerivedScanCodeAndPreservesUtcOrdering()
    {
        var json = JsonNode.Parse(ValidTrackingJson())!;
        var track = json["output"]!["completeTrackResults"]![0]!["trackResults"]![0]!;
        track["trackingNumberInfo"]!["carrierCode"] = "FDXE";
        track["serviceDetail"] = new JsonObject();
        track["latestStatusDetail"]!["scanLocation"] = new JsonObject();
        var scans = track["scanEvents"]!.AsArray();
        scans[0]!["eventType"] = "AR";
        scans[0]!["derivedStatusCode"] = "IT";
        scans.Add(scans[0]!.DeepClone());
        var result = FedExResponseAdmission.AdmitTracking(json.ToJsonString(), "123456789012");
        Assert.True(result.Success);
        Assert.Equal(2, result.Dto!.Events.Count);
        Assert.Equal(new DateTime(2026, 7, 8, 14, 15, 0, DateTimeKind.Utc), result.Dto.Events[0].Timestamp);
        Assert.Equal(ShipmentStatus.InTransit, result.Dto.Events[0].Status);
        track["error"] = new JsonObject { ["code"] = "TRACKING.DATA.NOTFOUND" };
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
    }

    [Fact]
    public void TrackingAdmission_ConflictingTerminalStatusAndMultipleResultsAreNotFinalTruth()
    {
        var json = JsonNode.Parse(ValidTrackingJson())!;
        var tracks = json["output"]!["completeTrackResults"]![0]!["trackResults"]!.AsArray();
        tracks[0]!["latestStatusDetail"]!["derivedCode"] = "DL";
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
        var scans = tracks[0]!["scanEvents"]!.AsArray();
        scans[0]!["eventType"] = "DL";
        Assert.True(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
        scans[1]!["date"] = scans[0]!["date"]!.GetValue<string>();
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
        tracks.Add(tracks[0]!.DeepClone());
        Assert.False(FedExResponseAdmission.AdmitTracking(json.ToJsonString()).Success);
    }

    [Fact]
    public void AvailabilityAdmission_UnknownDaysAndLocalDatesDoNotFabricateEstimates()
    {
        var json = JsonNode.Parse(ValidServiceAvailabilityJson())!;
        var commit = json["output"]!["transitTimes"]![0]!["transitTimeDetails"]![0]!["commit"]!;
        commit["transitDays"]!["minimumTransitTime"] = "UNKNOWN";
        commit["transitDays"]!["maximumTransitTime"] = "UNKNOWN";
        commit["dateDetail"] = new JsonObject { ["day"] = "Oct-15-2026", ["time"] = "17:00:00" };
        var result = FedExResponseAdmission.AdmitServiceAvailability(json.ToJsonString());
        Assert.True(result.Success);
        Assert.Null(result.Dto!.Services[0].MinimumTransitDays);
        Assert.Null(result.Dto.Services[0].MaximumTransitDays);
        Assert.Null(result.Dto.Services[0].EstimatedDeliveryDateUtc);
        json["output"]!["transitTimes"] = new JsonArray();
        Assert.False(FedExResponseAdmission.AdmitServiceAvailability(json.ToJsonString()).Success);
    }

    private static string ValidTrackingJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "completeTrackResults": [
              {
                "trackingNumber": "123456789012",
                "trackResults": [
                  {
                    "trackingNumberInfo": {
                      "trackingNumber": "123456789012"
                    },
                    "latestStatusDetail": {
                      "code": "AR",
                      "derivedCode": "IT",
                      "statusByLocale": "In transit",
                      "description": "In transit"
                    },
                    "scanEvents": [
                      {
                        "eventType": "IT",
                        "eventDescription": "In transit",
                        "date": "2026-07-08T09:15:00-05:00",
                        "scanLocation": {
                          "city": "Memphis",
                          "stateOrProvinceCode": "TN",
                          "countryCode": "US"
                        }
                      },
                      {
                        "eventType": "PU",
                        "eventDescription": "Picked up",
                        "date": "2026-07-07T16:00:00-05:00",
                        "scanLocation": {
                          "city": "Memphis",
                          "stateOrProvinceCode": "TN",
                          "countryCode": "US"
                        }
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

    private static string ValidServiceAvailabilityJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "transitTimes": [{ "transitTimeDetails": [
              {
                "serviceType": "FEDEX_2_DAY",
                "serviceName": "FedEx 2Day",
                "commit": { "transitDays": {
                  "minimumTransitTime": "TWO_DAYS",
                  "maximumTransitTime": "TWO_DAYS"
                } }
              },
              {
                "serviceType": "FEDEX_EXPRESS_SAVER",
                "serviceName": "FedEx Express Saver",
                "commit": { "transitDays": { "minimumTransitTime": "THREE_DAYS", "maximumTransitTime": "THREE_DAYS" } }
              }
            ] }],
            "alerts": [
              {
                "code": "NOTE",
                "message": "Availability returned",
                "alertType": "NOTE"
              }
            ]
          }
        }
        """;
}
