using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class RdvApiClient
{
    private readonly HttpClient _httpClient;

    public RdvApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, List<CreneauOccupe> Creneaux)> GetCreneauxAsync(Guid idEtablissement, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"etablissements/{idEtablissement}/creneaux?date={date:yyyy-MM-dd}";
            var creneaux = await _httpClient.GetFromJsonAsync<List<CreneauOccupe>>(url, cancellationToken);
            return (true, creneaux ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    /// <summary>Le mode de paiement est impose par la boutique et controle cote API.</summary>
    public async Task<(bool Success, string Status, RequestRdvWithPaiementResponse? Rdv)> RequestRdvAsync(
        Guid idClient, Guid idEtablissement, Guid idPrestation, DateTimeOffset dateHeureDebut, string? modePaiementChoisi, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "rdv")
            {
                Content = JsonContent.Create(new { idEtablissement, idPrestation, dateHeureDebut, modePaiementChoisi })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<RequestRdvWithPaiementResponse>(cancellationToken);
                return (true, body?.Statut ?? "created", body);
            }

            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error", null);
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }

    /// <summary>Feature 013 (US1) : historique complet des RDV du client, tries du plus recent au
    /// plus ancien (deja fait cote API).</summary>
    public async Task<(bool Success, List<RdvHistoriqueItem> Historique)> GetHistoriqueAsync(Guid idClient, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "rdv");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            var body = await response.Content.ReadFromJsonAsync<List<RdvHistoriqueItem>>(cancellationToken);
            return (true, body ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    /// <summary>Feature 015 (US1) : relancer un paiement en ligne echoue.</summary>
    public async Task<(bool Success, string? Status, string? LienPaiement)> RelancerPaiementAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/paiement/relancer");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, body?.GetValueOrDefault("status")?.ToString(), null);
            }

            return (true, body?.GetValueOrDefault("statutPaiement")?.ToString(), body?.GetValueOrDefault("lienPaiement")?.ToString());
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }

    /// <summary>Feature 015 (US2) : annulation par le client. RemboursementEnCours indique si une
    /// transaction REUSSIE a ete marquee REMBOURSEE (traitement manuel, jamais un virement reel
    /// automatique - voir specs/015-annulation-remboursement).</summary>
    public async Task<(bool Success, bool RemboursementEnCours)> AnnulerAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/annuler");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, false);
            }

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken);
            var remboursement = body is not null && body.TryGetValue("remboursementEnCours", out var r) && r is System.Text.Json.JsonElement je && je.GetBoolean();
            return (true, remboursement);
        }
        catch (Exception)
        {
            return (false, false);
        }
    }

    /// <summary>Feature 014 (US1) : laisser un avis sur un RDV termine.</summary>
    public async Task<(bool Success, string? Status)> LaisserAvisAsync(Guid idClient, Guid idRdv, short note, string? commentaire, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/avis")
            {
                Content = JsonContent.Create(new { note, commentaire })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? response.StatusCode.ToString());
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    /// <summary>Feature 013 (US3) : reconciliation manuelle d'un paiement RDV reste en attente.</summary>
    public async Task<(bool Success, string? StatutPaiement)> VerifierPaiementAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/paiement/verifier");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            var body = await response.Content.ReadFromJsonAsync<PaiementRdvInfo>(cancellationToken);
            return (true, body?.StatutPaiement);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, string Status, RdvHistoriqueItem? Rdv)> GetDetailAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"rdv/{idRdv}");
            request.Headers.Add("X-Client-Id", idClient.ToString());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (true, "ok", await response.Content.ReadFromJsonAsync<RdvHistoriqueItem>(cancellationToken));
            return (false, response.StatusCode == System.Net.HttpStatusCode.NotFound ? "not_found" : "server_error", null);
        }
        catch (Exception) { return (false, "network_error", null); }
    }
    public async Task<RdvStatutResponse?> GetStatutAsync(Guid idClient, Guid idRdv, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"rdv/{idRdv}");
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<RdvStatutResponse>(cancellationToken)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Status)> DeclarerLitigeAsync(Guid idClient, Guid idRdv, string motif, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/litige")
            {
                Content = JsonContent.Create(new { motif })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errorBody = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, errorBody?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception)
        {
            return (false, "network_error");
        }
    }

    /// <summary>Feature 018 (Parcours 3) : pourboire direct a la collaboratrice, 0% commission.</summary>
    public async Task<(bool Success, string? Status, string? LienPaiement)> LaisserPourboireAsync(
        Guid idClient, Guid idRdv, Guid idCollaborateur, decimal montant, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/pourboire")
            {
                Content = JsonContent.Create(new { idCollaborateur, montant })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, body?.GetValueOrDefault("status") ?? "error", null);
            }

            return (true, null, body?.GetValueOrDefault("lienPaiement"));
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }

    public async Task<(bool Success, string? Status)> ReprogrammerAsync(Guid idClient, Guid idRdv, DateTimeOffset dateHeureDebut, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/reprogrammer")
            {
                Content = JsonContent.Create(new { dateHeureDebut })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return (true, "reprogramme");
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, body?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception) { return (false, "network_error"); }
    }

    public async Task<(bool Success, string? Status)> SignalerRetardAsync(Guid idClient, Guid idRdv, int minutes, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"rdv/{idRdv}/retard")
            {
                Content = JsonContent.Create(new { minutes })
            };
            request.Headers.Add("X-Client-Id", idClient.ToString());
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return (true, "retard_signale");
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            return (false, body?.GetValueOrDefault("status") ?? "error");
        }
        catch (Exception) { return (false, "network_error"); }
    }
    public async Task<string?> GetQrDataUrlAsync(Guid idClient,Guid idRdv,CancellationToken cancellationToken)
    {
        try{using var request=new HttpRequestMessage(HttpMethod.Get,$"rdv/{idRdv}/qr");request.Headers.Add("X-Client-Id",idClient.ToString());var response=await _httpClient.SendAsync(request,cancellationToken);if(!response.IsSuccessStatusCode)return null;var svg=await response.Content.ReadAsStringAsync(cancellationToken);return $"data:image/svg+xml;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg))}";}catch{return null;}
    }
    public async Task<(bool Success,int BonusPoints,string? Status)> UploadReviewPhotosAsync(Guid idClient,Guid idRdv,byte[] before,string beforeName,byte[] after,string afterName,bool sharePublicly,CancellationToken cancellationToken)
    {
        try{using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(before),"photoAvant",beforeName);content.Add(new ByteArrayContent(after),"photoApres",afterName);content.Add(new StringContent(sharePublicly.ToString()),"partagerPubliquement");using var request=new HttpRequestMessage(HttpMethod.Post,$"rdv/{idRdv}/avis/photos"){Content=content};request.Headers.Add("X-Client-Id",idClient.ToString());var response=await _httpClient.SendAsync(request,cancellationToken);var text=await response.Content.ReadAsStringAsync(cancellationToken);if(!response.IsSuccessStatusCode)return(false,0,response.StatusCode.ToString());var body=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,System.Text.Json.JsonElement>>(text,new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true});return(true,body is not null&&body.TryGetValue("bonusPoints",out var p)?p.GetInt32():0,"saved");}catch{return(false,0,"network_error");}
    }}
