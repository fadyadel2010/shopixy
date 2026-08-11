using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Checkout;

public sealed class CheckoutFieldRuleEngineTests
{
    [Fact]
    public void Evaluate_ShowAndRequireRule_WhenConditionMatches_MakesTargetVisibleAndRequired()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Radio, "Need printing", true, false, OptionConfig()),
            CustomField(targetId, CheckoutFieldType.Image, "Upload design", true, false, RuleConfig(sourceId, "EQUALS", "yes", "SHOW", "REQUIRE"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [sourceId] = new("yes"),
            [targetId] = new(null)
        });

        Assert.True(states[targetId].Visible);
        Assert.True(states[targetId].Required);
    }

    [Fact]
    public void Evaluate_ShowAndRequireRule_WhenConditionDoesNotMatch_HidesTargetAndClearsRequired()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Radio, "Need printing", true, false, OptionConfig()),
            CustomField(targetId, CheckoutFieldType.Image, "Upload design", true, false, RuleConfig(sourceId, "EQUALS", "yes", "SHOW", "REQUIRE"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [sourceId] = new("no"),
            [targetId] = new(null)
        });

        Assert.False(states[targetId].Visible);
        Assert.False(states[targetId].Required);
    }

    [Fact]
    public void Evaluate_DisabledTarget_WithMatchingShowRule_RemainsHiddenAndOptional()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Radio, "Need printing", true, false, OptionConfig()),
            CustomField(targetId, CheckoutFieldType.Text, "Internal note", false, false, RuleConfig(sourceId, "EQUALS", "yes", "SHOW", "REQUIRE"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [sourceId] = new("yes"),
            [targetId] = new(null)
        });

        Assert.False(states[targetId].Visible);
        Assert.False(states[targetId].Required);
        Assert.False(states[targetId].Enabled);
    }

    [Fact]
    public void ValidateRules_RejectsSelfReference()
    {
        var fieldId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(fieldId, CheckoutFieldType.Text, "Company", true, false, RuleConfig(fieldId, "EQUALS", "yes", "SHOW"))
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CheckoutFieldRuleEngine.ValidateRules(
            fields,
            (code, message) => new InvalidOperationException(code)));

        Assert.Equal("FIELD_RULE_SELF_REFERENCE", ex.Message);
    }

    [Fact]
    public void ValidateRules_RejectsCircularDependencies()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(firstId, CheckoutFieldType.Text, "First", true, false, RuleConfig(secondId, "EQUALS", "yes", "SHOW")),
            CustomField(secondId, CheckoutFieldType.Text, "Second", true, false, RuleConfig(firstId, "EQUALS", "yes", "SHOW"))
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CheckoutFieldRuleEngine.ValidateRules(
            fields,
            (code, message) => new InvalidOperationException(code)));

        Assert.Equal("FIELD_RULE_CYCLE", ex.Message);
    }

    [Fact]
    public void ValidateRules_RejectsVisibilityActionConflicts()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Text, "Source", true, false, "{\"schemaVersion\":1}"),
            CustomField(targetId, CheckoutFieldType.Text, "Target", true, false,
                $"{{\"schemaVersion\":1,\"conditionalRules\":[{{\"ruleId\":\"one\",\"conditionMode\":\"ALL\",\"conditions\":[{{\"sourceFieldId\":\"{sourceId}\",\"operator\":\"EQUALS\",\"comparisonValue\":\"yes\"}}],\"actions\":[\"SHOW\"]}},{{\"ruleId\":\"two\",\"conditionMode\":\"ALL\",\"conditions\":[{{\"sourceFieldId\":\"{sourceId}\",\"operator\":\"EQUALS\",\"comparisonValue\":\"no\"}}],\"actions\":[\"HIDE\"]}}]}}")
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CheckoutFieldRuleEngine.ValidateRules(
            fields,
            (code, message) => new InvalidOperationException(code)));

        Assert.Equal("FIELD_RULE_ACTION_CONFLICT", ex.Message);
    }

    [Fact]
    public void Evaluate_HiddenSourceValue_DoesNotDriveDependentRule()
    {
        var controlId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(controlId, CheckoutFieldType.Text, "Control", true, false, "{\"schemaVersion\":1}"),
            CustomField(sourceId, CheckoutFieldType.Text, "Source", true, false, RuleConfig(controlId, "EQUALS", "show", "SHOW")),
            CustomField(targetId, CheckoutFieldType.Text, "Target", true, false, RuleConfig(sourceId, "EQUALS", "retained", "SHOW"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [controlId] = new("hide"),
            [sourceId] = new("retained"),
            [targetId] = new(null)
        });

        Assert.False(states[sourceId].Visible);
        Assert.False(states[targetId].Visible);
    }

    [Fact]
    public void Evaluate_NumberComparison_PreservesDecimalPrecision()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Number, "Number", true, false, "{\"schemaVersion\":1}"),
            CustomField(targetId, CheckoutFieldType.Text, "Target", true, false, RuleConfig(sourceId, "GREATER_THAN", "9007199254740992", "SHOW"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [sourceId] = new("9007199254740993"),
            [targetId] = new(null)
        });

        Assert.True(states[targetId].Visible);
    }

    [Fact]
    public void Evaluate_DateComparison_RejectsNonCanonicalOffsetDate()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fields = new[]
        {
            CustomField(sourceId, CheckoutFieldType.Date, "Date", true, false, "{\"schemaVersion\":1}"),
            CustomField(targetId, CheckoutFieldType.Text, "Target", true, false, RuleConfig(sourceId, "EQUALS", "2026-08-09", "SHOW"))
        };

        var states = CheckoutFieldRuleEngine.Evaluate(fields, new Dictionary<Guid, CheckoutFieldSubmittedValue>
        {
            [sourceId] = new("2026-08-09T23:00:00-02:00"),
            [targetId] = new(null)
        });

        Assert.False(states[targetId].Visible);
    }

    private static CheckoutFieldDefinition CustomField(Guid id, CheckoutFieldType type, string label, bool enabled, bool required, string configurationJson) => new()
    {
        Id = id,
        TenantId = Guid.NewGuid(),
        FieldKind = CheckoutFieldKind.Custom,
        FieldType = type,
        Label = label,
        IsEnabled = enabled,
        IsRequired = required,
        DisplayOrder = 10,
        ConfigurationSchemaVersion = 1,
        ConfigurationJson = configurationJson,
        CreatedAt = DateTime.UtcNow
    };

    private static string OptionConfig() => "{\"schemaVersion\":1,\"options\":[{\"value\":\"yes\",\"label\":\"Yes\"},{\"value\":\"no\",\"label\":\"No\"}]}";

    private static string RuleConfig(Guid sourceFieldId, string op, string value, params string[] actions) =>
        $"{{\"schemaVersion\":1,\"conditionalRules\":[{{\"ruleId\":\"rule1\",\"conditionMode\":\"ALL\",\"conditions\":[{{\"sourceFieldId\":\"{sourceFieldId}\",\"operator\":\"{op}\",\"comparisonValue\":\"{value}\"}}],\"actions\":[{string.Join(',', actions.Select(action => $"\"{action}\""))}]}}]}}";
}
