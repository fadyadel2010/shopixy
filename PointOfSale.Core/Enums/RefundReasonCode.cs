namespace PointOfSale.Core.Enums;

public enum RefundReasonCode
{
    DamagedItem = 0,
    NotAsDescribed = 1,
    WrongItem = 2,
    MissingParts = 3,
    QualityIssue = 4,
    Other = 5
}

public static class RefundReasonCodeExtensions
{
    public static string ToDisplayLabel(this RefundReasonCode reasonCode)
    {
        return reasonCode switch
        {
            RefundReasonCode.DamagedItem => "Damaged item",
            RefundReasonCode.NotAsDescribed => "Not as described",
            RefundReasonCode.WrongItem => "Wrong item",
            RefundReasonCode.MissingParts => "Missing parts",
            RefundReasonCode.QualityIssue => "Quality issue",
            _ => "Other"
        };
    }
}
