using System.Net.Http.Json;
using KekeBeauty.Web.Models;

namespace KekeBeauty.Web.Services;

public sealed class PartnerApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PartnerSessionService _session;

    public PartnerApiClient(HttpClient httpClient, PartnerSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    public async Task<(bool Success, bool Unauthorized, List<EtablissementGere> Etablissements)> GetMesEtablissementsAsync(Guid idPartner, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "partenaire/etablissements");
            await AddPartnerHeadersAsync(request, idPartner);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized, []);
            }

            var etablissements = await response.Content.ReadFromJsonAsync<List<EtablissementGere>>(cancellationToken);
            return (true, false, etablissements ?? []);
        }
        catch (Exception)
        {
            return (false, false, []);
        }
    }

    public async Task<(bool Success, EtablissementDetail? Detail)> GetDetailAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{idEtablissement}");
            await AddPartnerHeadersAsync(request, idPartner);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (true, await response.Content.ReadFromJsonAsync<EtablissementDetail>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, string? Status)> AddPrestationAsync(
        Guid idPartner, Guid idEtablissement, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/prestations",
            new { libellePrestation = libelle, tarif, dureeMinutes }, cancellationToken);
    }

    public Task<(bool Success, string? Status)> UpdateBoutiqueProfileAsync(Guid idPartner, Guid idEtablissement,
        string nom, string? description, string telephone, decimal latitude, decimal longitude,
        string? horaires, CancellationToken cancellationToken) =>
        SendPartnerRequestAsync(idPartner, HttpMethod.Put, $"partenaire/etablissements/{idEtablissement}/profil",
            new { nom, description, telephone, latitude, longitude, horaires }, cancellationToken);

    public async Task<List<IndisponibiliteDto>> GetIndisponibilitesAsync(Guid partner, Guid shop, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{shop}/indisponibilites");
        await AddPartnerHeadersAsync(request, partner);
        var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<List<IndisponibiliteDto>>(ct) ?? [] : [];
    }

    public Task<(bool Success, string? Status)> AddIndisponibiliteAsync(Guid partner, Guid shop, DateTimeOffset debut, DateTimeOffset fin, string? motif, CancellationToken ct) =>
        SendPartnerRequestAsync(partner, HttpMethod.Post, $"partenaire/etablissements/{shop}/indisponibilites", new { dateDebut=debut, dateFin=fin, motif }, ct);

    public Task<(bool Success, string? Status)> DeleteIndisponibiliteAsync(Guid partner, Guid shop, Guid id, CancellationToken ct) =>
        SendPartnerRequestAsync(partner, HttpMethod.Delete, $"partenaire/etablissements/{shop}/indisponibilites/{id}", null, ct);

    public async Task<bool> AddMediaAsync(Guid partner,Guid shop,Stream stream,string fileName,CancellationToken ct)
    { using var content=new MultipartFormDataContent();content.Add(new StreamContent(stream),"photo",fileName);using var request=new HttpRequestMessage(HttpMethod.Post,$"partenaire/etablissements/{shop}/medias"){Content=content};await AddPartnerHeadersAsync(request, partner);return (await _httpClient.SendAsync(request,ct)).IsSuccessStatusCode; }
    public Task<(bool Success,string? Status)> DeleteMediaAsync(Guid partner,Guid shop,Guid media,CancellationToken ct)=>SendPartnerRequestAsync(partner,HttpMethod.Delete,$"partenaire/etablissements/{shop}/medias/{media}",null,ct);
    public async Task<(bool Success,string? Status)> ResubmitKycAsync(Guid partner,Guid shop,Stream? photo,string? photoName,
        string typeDocument,Stream? recto,string? rectoName,Stream? verso,string? versoName,CancellationToken ct)
    {
        using var content=new MultipartFormDataContent(); content.Add(new StringContent(typeDocument),"typeDocumentIdentite");
        if(photo is not null)content.Add(new StreamContent(photo),"photoDevanture",photoName??"devanture.jpg");
        if(recto is not null)content.Add(new StreamContent(recto),"documentRecto",rectoName??"recto.jpg");
        if(verso is not null)content.Add(new StreamContent(verso),"documentVerso",versoName??"verso.jpg");
        using var request=new HttpRequestMessage(HttpMethod.Post,$"partenaire/etablissements/{shop}/kyc/resoumettre"){Content=content};await AddPartnerHeadersAsync(request,partner);
        var response=await _httpClient.SendAsync(request,ct);if(response.IsSuccessStatusCode)return(true,null);
        try{var body=await response.Content.ReadFromJsonAsync<Dictionary<string,string>>(ct);return(false,body?.GetValueOrDefault("status"));}catch{return(false,response.StatusCode.ToString());}
    }
    public async Task<PartnerProfileDto?> GetPartnerProfileAsync(Guid partner,CancellationToken ct){using var r=new HttpRequestMessage(HttpMethod.Get,"partenaire/profil");await AddPartnerHeadersAsync(r, partner);var x=await _httpClient.SendAsync(r,ct);return x.IsSuccessStatusCode?await x.Content.ReadFromJsonAsync<PartnerProfileDto>(ct):null;}
    public Task<(bool Success,string? Status)> UpdatePartnerProfileAsync(Guid partner,string nom,string? email,CancellationToken ct)=>SendPartnerRequestAsync(partner,HttpMethod.Put,"partenaire/profil",new{nom,email},ct);
    public async Task<bool> UploadPartnerPhotoAsync(Guid partner,byte[] bytes,string fileName,CancellationToken ct)
    { using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"photo",fileName);using var request=new HttpRequestMessage(HttpMethod.Post,"partenaire/profil/photo"){Content=content};await AddPartnerHeadersAsync(request,partner);return (await _httpClient.SendAsync(request,ct)).IsSuccessStatusCode; }
    public async Task<string?> GetPartnerPhotoDataUrlAsync(Guid partner,CancellationToken ct)
    { using var request=new HttpRequestMessage(HttpMethod.Get,"partenaire/profil/photo");await AddPartnerHeadersAsync(request,partner);var response=await _httpClient.SendAsync(request,ct);if(!response.IsSuccessStatusCode)return null;var bytes=await response.Content.ReadAsByteArrayAsync(ct);var mime=response.Content.Headers.ContentType?.MediaType??"image/jpeg";return $"data:{mime};base64,{Convert.ToBase64String(bytes)}"; }
    public async Task<List<AbonnementHistoriqueDto>> GetHistoriqueAbonnementsAsync(Guid partner,Guid shop,CancellationToken ct){using var r=new HttpRequestMessage(HttpMethod.Get,$"etablissements/{shop}/abonnements");await AddPartnerHeadersAsync(r, partner);var x=await _httpClient.SendAsync(r,ct);return x.IsSuccessStatusCode?await x.Content.ReadFromJsonAsync<List<AbonnementHistoriqueDto>>(ct)??[]:[];}

    public async Task<(bool Success, string? Status)> UpdatePrestationAsync(
        Guid idPartner, Guid idEtablissement, Guid idPrestation, string libelle, decimal tarif, short dureeMinutes, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Put, $"partenaire/etablissements/{idEtablissement}/prestations/{idPrestation}",
            new { libellePrestation = libelle, tarif, dureeMinutes }, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> DeletePrestationAsync(
        Guid idPartner, Guid idEtablissement, Guid idPrestation, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Delete, $"partenaire/etablissements/{idEtablissement}/prestations/{idPrestation}",
            body: null, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> UpdateModePaiementServiceAsync(
        Guid idPartner, Guid idEtablissement, string modePaiementService, bool paiementWave, bool paiementOrangeMoney, bool paiementMoovMoney, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Put, $"partenaire/etablissements/{idEtablissement}/paiement-services",
            new { modePaiementService, paiementWave, paiementOrangeMoney, paiementMoovMoney }, cancellationToken);
    }

    public async Task<(bool Success, PlanUsageDto? Usage)> GetPlanUsageAsync(
        Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"etablissements/{idEtablissement}/formule");
            await AddPartnerHeadersAsync(request, idPartner);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, await response.Content.ReadFromJsonAsync<PlanUsageDto>(cancellationToken))
                : (false, null);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    public async Task<(bool Success, List<TarifAbonnementDto> Tarifs)> GetTarifsAbonnementAsync(
        Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"etablissements/{idEtablissement}/tarifs-abonnement");
            await AddPartnerHeadersAsync(request, idPartner);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return (false, []);
            return (true, await response.Content.ReadFromJsonAsync<List<TarifAbonnementDto>>(cancellationToken) ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, List<PlanUsageDto> Formules)> GetFormulesAsync(
        Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"etablissements/{idEtablissement}/formules-disponibles");
            await AddPartnerHeadersAsync(request, idPartner);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return (false, []);
            return (true, await response.Content.ReadFromJsonAsync<List<PlanUsageDto>>(cancellationToken) ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, string? Status)> AssignCategoryAsync(
        Guid idPartner, Guid idEtablissement, string libelleCategorie, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/categories",
            new { libelleCategorie }, cancellationToken);
    }

    /// <summary>Feature 004 (onboarding) : soumission publique du dossier KYC (aucun compte
    /// requis au prealable - cree le compte PARTENAIRE et l'etablissement en un seul appel,
    /// cote API). Reutilise l'endpoint existant POST /partners/applications (multipart).</summary>
    public async Task<(bool Success, string? Status, Guid? IdEtablissement, string? Message)> SubmitOnboardingAsync(
        string telephone, string nomEtablissement, decimal gpsLatitude, decimal gpsLongitude,
        string numeroServiceClient, string? horaires, string modePaiementService, bool paiementWave, bool paiementOrangeMoney, bool paiementMoovMoney,
        Stream? photoDevanture, string? photoDevantureNomFichier, string typeDocumentIdentite,
        Stream? documentRecto, string? documentRectoNomFichier, Stream? documentVerso, string? documentVersoNomFichier, CancellationToken cancellationToken,
        string? googleOnboardingToken = null, string? categorie = null, string? partnerSessionToken = null)
    {
        try
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(telephone), "telephone" },
                { new StringContent(nomEtablissement), "nomEtablissement" },
                { new StringContent(gpsLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)), "gpsLatitude" },
                { new StringContent(gpsLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)), "gpsLongitude" },
                { new StringContent(numeroServiceClient), "numeroServiceClient" },
                { new StringContent(modePaiementService), "modePaiementService" },
                { new StringContent(paiementWave.ToString()), "paiementWave" },
                { new StringContent(paiementOrangeMoney.ToString()), "paiementOrangeMoney" },
                { new StringContent(paiementMoovMoney.ToString()), "paiementMoovMoney" },
                { new StringContent(typeDocumentIdentite), "typeDocumentIdentite" },
            };

            if (!string.IsNullOrWhiteSpace(horaires))
            {
                // Colonne etablissement.horaires typee JSONB (0001_init_schema.sql) : l'API caste
                // la valeur recue en ::jsonb telle quelle, donc un texte libre doit d'abord etre
                // encode comme chaine JSON valide (ex: "Lun-Sam 9h-19h" -> "\"Lun-Sam 9h-19h\"").
                content.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(horaires)), "horaires");
            }

            if (!string.IsNullOrWhiteSpace(googleOnboardingToken))
            {
                content.Add(new StringContent(googleOnboardingToken), "googleOnboardingToken");
            }

            if (!string.IsNullOrWhiteSpace(categorie))
            {
                content.Add(new StringContent(categorie), "categorie");
            }

            if (photoDevanture is not null && photoDevantureNomFichier is not null)
            {
                content.Add(new StreamContent(photoDevanture), "photoDevanture", photoDevantureNomFichier);
            }

            if (documentRecto is not null && documentRectoNomFichier is not null)
            {
                content.Add(new StreamContent(documentRecto), "documentRecto", documentRectoNomFichier);
            }

            if (documentVerso is not null && documentVersoNomFichier is not null)
            {
                content.Add(new StreamContent(documentVerso), "documentVerso", documentVersoNomFichier);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "partners/applications") { Content = content };
            if (!string.IsNullOrWhiteSpace(partnerSessionToken)) request.Headers.TryAddWithoutValidation("X-Partner-Token", partnerSessionToken);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = TryReadJson(responseText);
                var fallback = string.IsNullOrWhiteSpace(responseText)
                    ? "L'API n'a retourne aucun detail pour cette erreur."
                    : responseText;
                return (false, errorBody?.GetValueOrDefault("status")?.ToString() ?? response.StatusCode.ToString(), null,
                    errorBody?.GetValueOrDefault("message")?.ToString() ?? fallback);
            }

            var body = TryReadJson(responseText);
            if (body is null)
            {
                return (false, "empty_response", null, "L'API a confirme la requete sans retourner les informations de la boutique.");
            }
            var idEtablissement = body.TryGetValue("idEtablissement", out var idValue) && Guid.TryParse(idValue.ToString(), out var id) ? id : (Guid?)null;            return (true, "created", idEtablissement, null);
        }
        catch (Exception exception)
        {
            return (false, "network_error", null, $"Erreur de communication : {exception.Message}");
        }
    }

    private static Dictionary<string, object>? TryReadJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(content,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
    /// <summary>Feature 008 (abonnement) : souscription depuis l'espace partenaire. Reutilise
    /// l'endpoint existant POST /etablissements/{id}/abonnements (deja fonctionnel, WinPayer TEST).</summary>
    public async Task<(bool Success, string? Status, string? CheckoutUrl)> SubscribeAsync(
        Guid idPartner, Guid idEtablissement, string periodicite, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"etablissements/{idEtablissement}/abonnements")
            {
                Content = JsonContent.Create(new { periodicite })
            };
            await AddPartnerHeadersAsync(request, idPartner);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, body?.GetValueOrDefault("status")?.ToString() ?? response.StatusCode.ToString(), null);
            }

            return (true, body?.GetValueOrDefault("statut")?.ToString(), body?.GetValueOrDefault("checkoutUrl")?.ToString());
        }
        catch (Exception)
        {
            return (false, "network_error", null);
        }
    }

    /// <summary>Statistiques en lecture seule pour l'ecran "Revenus & Statistiques Pro".</summary>
    public async Task<(bool Success, PartenaireStatistiquesDto? Stats)> GetStatistiquesAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{idEtablissement}/statistiques");
            await AddPartnerHeadersAsync(request, idPartner);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            return (true, await response.Content.ReadFromJsonAsync<PartenaireStatistiquesDto>(cancellationToken));
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    /// <summary>Prestations de l'etablissement, reutilise la fiche complete (meme source que
    /// l'annuaire public) au lieu d'un endpoint dedie - donnees reelles, aucune duplication.</summary>
    public async Task<(bool Success, List<PrestationItem> Prestations)> ListPrestationsAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        var (success, detail) = await GetDetailAsync(idPartner, idEtablissement, cancellationToken);
        if (!success || detail is null)
        {
            return (false, []);
        }

        var prestations = detail.Prestations
            .Select(p => new PrestationItem { IdPrestation = p.IdPrestation, Libelle = p.LibellePrestation, Tarif = p.Tarif, DureeMinutes = p.DureeMinutes })
            .ToList();
        return (true, prestations);
    }

    /// <summary>Alias leger de GetEquipeAsync (memes donnees reelles) pour les ecrans qui n'ont
    /// besoin que d'Id/Nom/Specialite (comptoir, planning desktop).</summary>
    public async Task<(bool Success, List<CollaborateurItem> Collaborateurs)> ListCollaborateursAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        var (success, equipe) = await GetEquipeAsync(idPartner, idEtablissement, cancellationToken);
        if (!success)
        {
            return (false, []);
        }

        var collaborateurs = equipe
            .Select(c => new CollaborateurItem { IdCollaborateur = c.IdCollaborateur, Nom = c.Nom, Specialite = c.Specialite })
            .ToList();
        return (true, collaborateurs);
    }

    /// <summary>Feature 016 (equipe) : referencement des collaboratrices d'un etablissement
    /// (perimetre reduit, pas de compte de connexion - voir specs/016-gestion-equipe/spec.md).</summary>
    public async Task<(bool Success, List<CollaborateurDto> Equipe)> GetEquipeAsync(Guid idPartner, Guid idEtablissement, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"partenaire/etablissements/{idEtablissement}/collaborateurs");
            await AddPartnerHeadersAsync(request, idPartner);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            var equipe = await response.Content.ReadFromJsonAsync<List<CollaborateurDto>>(cancellationToken);
            return (true, equipe ?? []);
        }
        catch (Exception)
        {
            return (false, []);
        }
    }

    public async Task<(bool Success, string? Status)> AjouterCollaborateurAsync(
        Guid idPartner, Guid idEtablissement, string nom, string? specialite, string? telephone, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/collaborateurs",
            new { nom, specialite, telephone }, cancellationToken);
    }

    public async Task<(bool Success, string? Status)> RetirerCollaborateurAsync(
        Guid idPartner, Guid idEtablissement, Guid idCollaborateur, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Delete, $"partenaire/etablissements/{idEtablissement}/collaborateurs/{idCollaborateur}",
            body: null, cancellationToken);
    }

    /// <summary>Feature 018 (Parcours 5) : le gerant assigne une collaboratrice a un RDV (planning
    /// individuel). idCollaborateur == null retire l'assignation.</summary>
    public async Task<(bool Success, string? Status)> AssignerCollaborateurAsync(
        Guid idPartner, Guid idEtablissement, Guid idRdv, Guid? idCollaborateur, CancellationToken cancellationToken)
    {
        return await SendPartnerRequestAsync(idPartner, HttpMethod.Post, $"partenaire/etablissements/{idEtablissement}/rdv/{idRdv}/assigner",
            new { idCollaborateur }, cancellationToken);
    }

    private async Task AddPartnerHeadersAsync(HttpRequestMessage request, Guid idPartner)
    {
        request.Headers.TryAddWithoutValidation("X-Partner-Id", idPartner.ToString());
        await Task.CompletedTask;
    }
    private async Task<(bool Success, string? Status)> SendPartnerRequestAsync(
        Guid idPartner, HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            await AddPartnerHeadersAsync(request, idPartner);

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
}
