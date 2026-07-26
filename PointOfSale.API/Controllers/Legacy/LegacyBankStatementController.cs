using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PointOfSale.API.Controllers;

/// <summary>
/// PHASE LTSR: Legacy Bank Statement Controller
/// 
/// This controller has been retired. All balance and transaction data
/// should now be read from the Ledger system via TreasuryController.
/// 
/// ⚠️ Returns HTTP 410 (Gone) for read endpoints.
/// 
/// Migration targets:
/// - Balance: GET /api/treasury/balance/1030
/// - Statement: GET /api/treasury/movements/1030
/// </summary>
[ApiController]
[Route("api/bank-statement")]
[Authorize]
[Obsolete("Phase LTSR: Use /api/treasury/balance/1030 and /api/treasury/movements/1030 instead")]
public class LegacyBankStatementController : ControllerBase
{
    private const string LegacyMessage = 
        "⚠️ LEGACY ENDPOINT RETIRED (Phase LTSR) - " +
        "This endpoint has been replaced by the Ledger-based Treasury API. " +
        "Use /api/treasury/balance/1030 for Bank balance, " +
        "or /api/treasury/movements/1030 for Bank statement.";

    /// <summary>
    /// RETIRED: Get bank statement
    /// </summary>
    [HttpGet]
    public IActionResult GetStatement()
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/movements/1030",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Get bank balance
    /// </summary>
    [HttpGet("balance")]
    public IActionResult GetBalance()
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/balance/1030",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Get bank balance for a specific bank
    /// </summary>
    [HttpGet("{bankId}/balance")]
    public IActionResult GetBankBalance(Guid bankId)
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/balance/1030",
            phase = "LTSR"
        });

    /// <summary>
    /// RETIRED: Get statement for a specific bank
    /// </summary>
    [HttpGet("{bankId}")]
    public IActionResult GetBankStatement(Guid bankId)
        => StatusCode(410, new { 
            message = LegacyMessage,
            redirect = "/api/treasury/movements/1030",
            phase = "LTSR"
        });
}
