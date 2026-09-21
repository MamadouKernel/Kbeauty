using KekeBeauty.Api.Auth;
using KekeBeauty.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace KekeBeauty.Api.Controllers;

/// <summary>
/// Tableau de bord global Super-Admin (feature 017, Parcours 6 perimetre reduit - voir
/// specs/017-admin-analytics/spec.md). Reutilise AdminApiKeyFilter existant (004).
/// </summary>
[ApiController]
[Route("admin/statistiques")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
public sealed class AdminAnalyticsController : ControllerBase
{
    private readonly IAdminStatsRepository _repository;

    public AdminAnalyticsController(IAdminStatsRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var stats = await _repository.GetStatistiquesGlobalesAsync(cancellationToken);
        return Ok(stats);
    }
}
