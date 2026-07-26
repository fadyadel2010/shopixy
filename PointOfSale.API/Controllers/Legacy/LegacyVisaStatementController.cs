using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PointOfSale.API.Controllers;

/// <summary>
/// PHASE LTSR: Legacy Visa Statement Controller
/// 
/// This controller has been retired. All balance and transaction data
/// should now be read from the Ledger system via TreasuryController.
/// 
/// ⚠️ Returns HTTP 410 (Gone) for all endpoints.
/// 
/// Migration targets:
/// - Balance: GET /api/treasury/balance/1110
/// - Statement: GET /api/treasury/movements/1110
/// </summary>
[ApiController]
[Route("api/visa")]
[Authorize]
[Obsolete("Phase LTSR: Use /api/treasury/balance/1110 and /api/treasury/movements/1110 instead")]
public class LegacyVisaStatementController : ControllerBase
{
    private const string LegacyMessage = 
        "⚠️ LEGACY ENDPOINT RETIRED (Phase LTSR) - " +
        "This endpoint has been replaced by the Ledger-based Treasury API. " +
        "Use /api/treasury/balance/1110 for Visa balance, " +
        "or /api/treasury/movements/1110 for Visa statement.";

    /// <summary>
    /// RETIRED: Get Visa account statement
    /// </summary>
    [HttpGet("statement")]
    public IActionResult GetStatement()
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/movements/1110",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Get current Visa balance
    /// </summary>
    [HttpGet("balance")]
    public IActionResult GetBalance()
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/balance/1110",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Get transaction details
    /// </summary>
    [HttpGet("transactions/{id}")]
    public IActionResult GetTransactionDetails(Guid id)
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = $"/api/treasury/movements/1110",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Record new Visa transaction
    /// ⚠️ This will be replaced by Journal posting in Phase TOJ
    /// </summary>
    [HttpPost("transaction")]
    public IActionResult RecordTransaction()
        => StatusCode(410, new { 
            message = LegacyMessage + " Transaction recording will move to journal-based operations.",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Set opening balance
    /// ⚠️ Opening balances are now Ledger journal entries
    /// </summary>
    [HttpPost("opening-balance")]
    public IActionResult SetOpeningBalance()
        => StatusCode(410, new { 
            message = LegacyMessage + " Opening balances are now set via Ledger journal entries.",
            phase = "LTSR"
        });
}
