using System.Globalization;
using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Checkout;

public static class CheckoutFieldRuleEngine
{
    private const NumberStyles CanonicalNumberStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    public const string ConditionModeAll = "ALL";
    public const string ConditionModeAny = "ANY";
    public const string ActionShow = "SHOW";
    public const string ActionHide = "HIDE";
    public const string ActionRequire = "REQUIRE";
    public const string ActionOptional = "OPTIONAL";
    public const string OperatorEquals = "EQUALS";
    public const string OperatorNotEquals = "NOT_EQUALS";
    public const string OperatorContains = "CONTAINS";
    public const string OperatorNotContains = "NOT_CONTAINS";
    public const string OperatorIsEmpty = "IS_EMPTY";
    public const string OperatorIsNotEmpty = "IS_NOT_EMPTY";
    public const string OperatorGreaterThan = "GREATER_THAN";
    public const string OperatorGreaterThanOrEqual = "GREATER_THAN_OR_EQUAL";
    public const string OperatorLessThan = "LESS_THAN";
    public const string OperatorLessThanOrEqual = "LESS_THAN_OR_EQUAL";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ActionShow,
        ActionHide,
        ActionRequire,
        ActionOptional
    };

    private static readonly HashSet<string> SupportedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        OperatorEquals,
        OperatorNotEquals,
        OperatorContains,
        OperatorNotContains,
        OperatorIsEmpty,
        OperatorIsNotEmpty,
        OperatorGreaterThan,
        OperatorGreaterThanOrEqual,
        OperatorLessThan,
        OperatorLessThanOrEqual
    };

    public static IReadOnlyDictionary<Guid, CheckoutFieldEffectiveState> Evaluate(
        IReadOnlyList<CheckoutFieldDefinition> definitions,
        IReadOnlyDictionary<Guid, CheckoutFieldSubmittedValue> valuesByFieldId)
    {
        var fieldsById = definitions.ToDictionary(field => field.Id);
        var states = definitions.ToDictionary(
            field => field.Id,
            field => new CheckoutFieldEffectiveState(
                field.Id,
                field.IsEnabled,
                field.IsEnabled && field.IsRequired,
                field.IsEnabled));
        var evaluated = new HashSet<Guid>();
        var evaluating = new HashSet<Guid>();

        foreach (var target in definitions)
        {
            EvaluateField(target);
        }

        return states;

        void EvaluateField(CheckoutFieldDefinition target)
        {
            if (!evaluated.Add(target.Id))
            {
                return;
            }

            if (!target.IsEnabled)
            {
                return;
            }

            var rules = ReadRules(target.ConfigurationJson);
            if (rules.Count == 0)
            {
                return;
            }

            if (!evaluating.Add(target.Id))
            {
                return;
            }
            foreach (var sourceFieldId in rules.SelectMany(rule => rule.Conditions).Select(condition => condition.SourceFieldId).Distinct())
            {
                if (fieldsById.TryGetValue(sourceFieldId, out var source))
                {
                    EvaluateField(source);
                }
            }
            evaluating.Remove(target.Id);

            var hasShowRule = rules.Any(rule => rule.Actions.Contains(ActionShow, StringComparer.OrdinalIgnoreCase));
            var showMatched = false;
            var hideMatched = false;
            var requireMatched = false;
            var optionalMatched = false;

            foreach (var rule in rules)
            {
                var matched = RuleMatches(rule, fieldsById, valuesByFieldId, states);
                if (!matched)
                {
                    continue;
                }

                showMatched |= rule.Actions.Contains(ActionShow, StringComparer.OrdinalIgnoreCase);
                hideMatched |= rule.Actions.Contains(ActionHide, StringComparer.OrdinalIgnoreCase);
                requireMatched |= rule.Actions.Contains(ActionRequire, StringComparer.OrdinalIgnoreCase);
                optionalMatched |= rule.Actions.Contains(ActionOptional, StringComparer.OrdinalIgnoreCase);
            }

            var visible = hasShowRule ? showMatched : target.IsEnabled;
            if (hideMatched)
            {
                visible = false;
            }

            var required = target.IsRequired;
            if (requireMatched)
            {
                required = true;
            }
            if (optionalMatched)
            {
                required = false;
            }
            if (!visible)
            {
                required = false;
            }

            states[target.Id] = new CheckoutFieldEffectiveState(target.Id, visible, required, target.IsEnabled);
        }
    }

    public static IReadOnlyList<CheckoutFieldRuleDefinition> ReadRules(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return Array.Empty<CheckoutFieldRuleDefinition>();
        }

        var configuration = JsonSerializer.Deserialize<CheckoutFieldRulesConfiguration>(configurationJson, JsonOptions);
        return configuration?.ConditionalRules?.Where(rule => rule != null).ToArray()
            ?? Array.Empty<CheckoutFieldRuleDefinition>();
    }

    public static void ValidateRules(
        IReadOnlyList<CheckoutFieldDefinition> definitions,
        Func<string, string, Exception> exceptionFactory)
    {
        var fieldsById = definitions.ToDictionary(field => field.Id);
        var edges = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var target in definitions.Where(field => !field.IsDeleted))
        {
            IReadOnlyList<CheckoutFieldRuleDefinition> rules;
            try
            {
                rules = ReadRules(target.ConfigurationJson);
            }
            catch (JsonException)
            {
                throw exceptionFactory("FIELD_RULES_MALFORMED", "Checkout field conditional rules are malformed.");
            }

            if (rules.Count == 0)
            {
                continue;
            }

            if (!target.IsEnabled)
            {
                throw exceptionFactory("FIELD_RULE_TARGET_DISABLED", "Conditional rules can only target enabled checkout fields.");
            }

            if (rules.Count > 25)
            {
                throw exceptionFactory("FIELD_RULES_TOO_MANY", "A checkout field can define at most 25 conditional rules.");
            }

            var targetActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                ValidateRule(target, rule, fieldsById, targetActions, exceptionFactory);
                foreach (var sourceFieldId in rule.Conditions.Select(condition => condition.SourceFieldId).Distinct())
                {
                    if (!edges.TryGetValue(sourceFieldId, out var targets))
                    {
                        targets = new HashSet<Guid>();
                        edges[sourceFieldId] = targets;
                    }

                    targets.Add(target.Id);
                }
            }

            if (targetActions.Contains(ActionShow) && targetActions.Contains(ActionHide))
            {
                throw exceptionFactory("FIELD_RULE_ACTION_CONFLICT", "A checkout field cannot mix SHOW and HIDE conditional rules.");
            }

            if (targetActions.Contains(ActionRequire) && targetActions.Contains(ActionOptional))
            {
                throw exceptionFactory("FIELD_RULE_ACTION_CONFLICT", "A checkout field cannot mix REQUIRE and OPTIONAL conditional rules.");
            }
        }

        RejectCycles(edges, exceptionFactory);
    }

    private static void ValidateRule(
        CheckoutFieldDefinition target,
        CheckoutFieldRuleDefinition rule,
        IReadOnlyDictionary<Guid, CheckoutFieldDefinition> fieldsById,
        ISet<string> targetActions,
        Func<string, string, Exception> exceptionFactory)
    {
        if (!string.Equals(rule.ConditionMode, ConditionModeAll, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(rule.ConditionMode, ConditionModeAny, StringComparison.OrdinalIgnoreCase))
        {
            throw exceptionFactory("FIELD_RULE_MODE_INVALID", "Conditional rule mode must be ALL or ANY.");
        }

        if (rule.Actions == null || rule.Actions.Count == 0)
        {
            throw exceptionFactory("FIELD_RULE_ACTION_REQUIRED", "Conditional rule requires at least one action.");
        }

        if (rule.Actions.Count > 2)
        {
            throw exceptionFactory("FIELD_RULE_ACTION_INVALID", "Conditional rule supports at most two actions.");
        }

        foreach (var action in rule.Actions.Select(NormalizeToken))
        {
            if (!SupportedActions.Contains(action))
            {
                throw exceptionFactory("FIELD_RULE_ACTION_INVALID", "Conditional rule action is not supported.");
            }

            targetActions.Add(action);
        }

        if (rule.Conditions == null || rule.Conditions.Count == 0)
        {
            throw exceptionFactory("FIELD_RULE_CONDITION_REQUIRED", "Conditional rule requires at least one condition.");
        }

        if (rule.Conditions.Count > 5)
        {
            throw exceptionFactory("FIELD_RULE_CONDITIONS_TOO_MANY", "A conditional rule can define at most five conditions.");
        }

        foreach (var condition in rule.Conditions)
        {
            ValidateCondition(target, condition, fieldsById, exceptionFactory);
        }
    }

    private static void ValidateCondition(
        CheckoutFieldDefinition target,
        CheckoutFieldRuleCondition condition,
        IReadOnlyDictionary<Guid, CheckoutFieldDefinition> fieldsById,
        Func<string, string, Exception> exceptionFactory)
    {
        if (condition.SourceFieldId == Guid.Empty)
        {
            throw exceptionFactory("FIELD_RULE_SOURCE_INVALID", "Conditional rule source field is required.");
        }

        if (condition.SourceFieldId == target.Id)
        {
            throw exceptionFactory("FIELD_RULE_SELF_REFERENCE", "A checkout field cannot conditionally depend on itself.");
        }

        if (!fieldsById.TryGetValue(condition.SourceFieldId, out var source) || source.IsDeleted || !source.IsEnabled)
        {
            throw exceptionFactory("FIELD_RULE_SOURCE_INVALID", "Conditional rule source field was not found for the current tenant.");
        }

        var op = NormalizeToken(condition.Operator);
        if (!SupportedOperators.Contains(op))
        {
            throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "Conditional rule operator is not supported.");
        }

        if (op is OperatorIsEmpty or OperatorIsNotEmpty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(condition.ComparisonValue))
        {
            throw exceptionFactory("FIELD_RULE_COMPARISON_REQUIRED", "Conditional rule comparison value is required for the selected operator.");
        }

        ValidateComparisonValue(source, op, condition.ComparisonValue!, exceptionFactory);
    }

    private static void ValidateComparisonValue(
        CheckoutFieldDefinition source,
        string op,
        string comparisonValue,
        Func<string, string, Exception> exceptionFactory)
    {
        switch (source.FieldType)
        {
            case CheckoutFieldType.Select:
            case CheckoutFieldType.Radio:
                if (op is not (OperatorEquals or OperatorNotEquals or OperatorContains or OperatorNotContains))
                {
                    throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "The selected operator is not valid for option fields.");
                }

                var options = ReadOptions(source.ConfigurationJson);
                if (!options.Contains(comparisonValue.Trim(), StringComparer.Ordinal))
                {
                    throw exceptionFactory("FIELD_RULE_COMPARISON_INVALID", "Conditional rule comparison value must match a configured option value.");
                }
                break;

            case CheckoutFieldType.Checkbox:
                if (op is not (OperatorEquals or OperatorNotEquals))
                {
                    throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "The selected operator is not valid for checkbox fields.");
                }
                if (!IsBooleanValue(comparisonValue))
                {
                    throw exceptionFactory("FIELD_RULE_COMPARISON_INVALID", "Checkbox comparison value must be true or false.");
                }
                break;

            case CheckoutFieldType.Number:
                if (op is OperatorContains or OperatorNotContains)
                {
                    throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "Contains operators are not valid for number fields.");
                }
                if (!decimal.TryParse(comparisonValue, CanonicalNumberStyles, CultureInfo.InvariantCulture, out _))
                {
                    throw exceptionFactory("FIELD_RULE_COMPARISON_INVALID", "Number comparison value must be numeric.");
                }
                break;

            case CheckoutFieldType.Date:
                if (op is OperatorContains or OperatorNotContains)
                {
                    throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "Contains operators are not valid for date fields.");
                }
                if (!DateOnly.TryParseExact(comparisonValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    throw exceptionFactory("FIELD_RULE_COMPARISON_INVALID", "Date comparison value must be a valid date.");
                }
                break;

            case CheckoutFieldType.Image:
            case CheckoutFieldType.File:
            case CheckoutFieldType.MultiImage:
                throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "Upload fields only support empty/not empty predicates.");

            default:
                if (op is OperatorGreaterThan or OperatorGreaterThanOrEqual or OperatorLessThan or OperatorLessThanOrEqual)
                {
                    throw exceptionFactory("FIELD_RULE_OPERATOR_INVALID", "Numeric/date comparison operators are not valid for text fields.");
                }
                break;
        }
    }

    private static bool RuleMatches(
        CheckoutFieldRuleDefinition rule,
        IReadOnlyDictionary<Guid, CheckoutFieldDefinition> fieldsById,
        IReadOnlyDictionary<Guid, CheckoutFieldSubmittedValue> valuesByFieldId,
        IReadOnlyDictionary<Guid, CheckoutFieldEffectiveState> states)
    {
        var results = rule.Conditions.Select(condition =>
        {
            if (!fieldsById.TryGetValue(condition.SourceFieldId, out var source) ||
                !valuesByFieldId.TryGetValue(condition.SourceFieldId, out var value))
            {
                return false;
            }

            var effectiveValue = states.TryGetValue(source.Id, out var sourceState) && !sourceState.Visible
                ? new CheckoutFieldSubmittedValue(null)
                : value;
            return ConditionMatches(source, condition, effectiveValue);
        });

        return string.Equals(rule.ConditionMode, ConditionModeAny, StringComparison.OrdinalIgnoreCase)
            ? results.Any(result => result)
            : results.All(result => result);
    }

    private static bool ConditionMatches(
        CheckoutFieldDefinition source,
        CheckoutFieldRuleCondition condition,
        CheckoutFieldSubmittedValue submittedValue)
    {
        var op = NormalizeToken(condition.Operator);
        if (op == OperatorIsEmpty)
        {
            return submittedValue.IsEmpty(source.FieldType);
        }
        if (op == OperatorIsNotEmpty)
        {
            return !submittedValue.IsEmpty(source.FieldType);
        }

        var left = submittedValue.ScalarValue?.Trim();
        var right = condition.ComparisonValue?.Trim();
        if (left == null || right == null)
        {
            return op == OperatorNotEquals || op == OperatorNotContains;
        }

        return source.FieldType switch
        {
            CheckoutFieldType.Checkbox => CompareBoolean(left, right, op),
            CheckoutFieldType.Number => CompareDecimal(left, right, op),
            CheckoutFieldType.Date => CompareDate(left, right, op),
            CheckoutFieldType.Select or CheckoutFieldType.Radio => CompareString(left, right, op, StringComparison.Ordinal),
            _ => CompareString(left, right, op, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool CompareString(string left, string right, string op, StringComparison comparison) => op switch
    {
        OperatorEquals => string.Equals(left, right, comparison),
        OperatorNotEquals => !string.Equals(left, right, comparison),
        OperatorContains => left.Contains(right, comparison),
        OperatorNotContains => !left.Contains(right, comparison),
        _ => false
    };

    private static bool CompareBoolean(string left, string right, string op)
    {
        var leftValue = IsTruthyCheckboxValue(left);
        var rightValue = IsTruthyCheckboxValue(right);
        return op == OperatorEquals ? leftValue == rightValue : op == OperatorNotEquals && leftValue != rightValue;
    }

    private static bool CompareDecimal(string left, string right, string op)
    {
        if (!decimal.TryParse(left, CanonicalNumberStyles, CultureInfo.InvariantCulture, out var leftNumber) ||
            !decimal.TryParse(right, CanonicalNumberStyles, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return false;
        }

        return op switch
        {
            OperatorEquals => leftNumber == rightNumber,
            OperatorNotEquals => leftNumber != rightNumber,
            OperatorGreaterThan => leftNumber > rightNumber,
            OperatorGreaterThanOrEqual => leftNumber >= rightNumber,
            OperatorLessThan => leftNumber < rightNumber,
            OperatorLessThanOrEqual => leftNumber <= rightNumber,
            _ => false
        };
    }

    private static bool CompareDate(string left, string right, string op)
    {
        if (!DateOnly.TryParseExact(left, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var leftDate) ||
            !DateOnly.TryParseExact(right, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rightDate))
        {
            return false;
        }

        return op switch
        {
            OperatorEquals => leftDate == rightDate,
            OperatorNotEquals => leftDate != rightDate,
            OperatorGreaterThan => leftDate > rightDate,
            OperatorGreaterThanOrEqual => leftDate >= rightDate,
            OperatorLessThan => leftDate < rightDate,
            OperatorLessThanOrEqual => leftDate <= rightDate,
            _ => false
        };
    }

    private static IReadOnlyList<string> ReadOptions(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return Array.Empty<string>();
        }

        var configuration = JsonSerializer.Deserialize<CheckoutFieldRulesConfiguration>(configurationJson, JsonOptions);
        return configuration?.Options?.Select(option => option.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            ?? Array.Empty<string>();
    }

    private static void RejectCycles(
        IReadOnlyDictionary<Guid, HashSet<Guid>> edges,
        Func<string, string, Exception> exceptionFactory)
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();

        foreach (var node in edges.Keys)
        {
            Visit(node);
        }

        void Visit(Guid node)
        {
            if (visited.Contains(node))
            {
                return;
            }

            if (!visiting.Add(node))
            {
                throw exceptionFactory("FIELD_RULE_CYCLE", "Conditional checkout field dependencies must be acyclic.");
            }

            if (edges.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    Visit(target);
                }
            }

            visiting.Remove(node);
            visited.Add(node);
        }
    }

    private static string NormalizeToken(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsBooleanValue(string value) =>
        bool.TryParse(value, out _) || value == "1" || value == "0";

    private static bool IsTruthyCheckboxValue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private sealed class CheckoutFieldRulesConfiguration
    {
        public List<CheckoutFieldOptionForRules>? Options { get; set; }
        public List<CheckoutFieldRuleDefinition>? ConditionalRules { get; set; }
    }

    private sealed class CheckoutFieldOptionForRules
    {
        public string Value { get; set; } = string.Empty;
    }
}

public sealed record CheckoutFieldRuleDefinition
{
    public string RuleId { get; init; } = string.Empty;
    public string ConditionMode { get; init; } = CheckoutFieldRuleEngine.ConditionModeAll;
    public List<CheckoutFieldRuleCondition> Conditions { get; init; } = new();
    public List<string> Actions { get; init; } = new();
}

public sealed record CheckoutFieldRuleCondition
{
    public Guid SourceFieldId { get; init; }
    public string Operator { get; init; } = string.Empty;
    public string? ComparisonValue { get; init; }
}

public sealed record CheckoutFieldSubmittedValue(string? ScalarValue, IReadOnlyCollection<Guid>? AttachmentIds = null)
{
    public bool IsEmpty(CheckoutFieldType fieldType) => fieldType switch
    {
        CheckoutFieldType.Checkbox => !(
            string.Equals(ScalarValue, "true", StringComparison.OrdinalIgnoreCase) || ScalarValue == "1"),
        CheckoutFieldType.Image or CheckoutFieldType.File or CheckoutFieldType.MultiImage => AttachmentIds == null || AttachmentIds.Count == 0,
        _ => string.IsNullOrWhiteSpace(ScalarValue)
    };
}

public sealed record CheckoutFieldEffectiveState(Guid FieldId, bool Visible, bool Required, bool Enabled);
