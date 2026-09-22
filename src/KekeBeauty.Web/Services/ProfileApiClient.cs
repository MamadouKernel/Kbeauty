using System.Net.Http.Json;
using KekeBeauty.Web.Models;
namespace KekeBeauty.Web.Services;
public sealed class ProfileApiClient
{
    private readonly HttpClient _http; private readonly ClientSessionService _session; public ProfileApiClient(HttpClient http,ClientSessionService session){_http=http;_session=session;}
    // Le jeton est attache ici (typed client, scope DI correct du circuit) plutot que dans
    // ClientAuthHandler : IHttpClientFactory construit les DelegatingHandler via un scope DI mis
    // en cache/tourniquet distinct du circuit Blazor courant, donc ProtectedLocalStorage
    // (IJSRuntime) y echoue silencieusement - le jeton n'atteint jamais l'API.
    private async Task<HttpRequestMessage> Request(HttpMethod method,string url,Guid id,object? body=null){var r=new HttpRequestMessage(method,url);r.Headers.Add("X-Client-Id",id.ToString());var token=await _session.GetTokenAsync();if(!string.IsNullOrWhiteSpace(token))r.Headers.TryAddWithoutValidation("X-Client-Token",token);if(body is not null)r.Content=JsonContent.Create(body);return r;}
    public async Task<(bool,ClientProfile?)> GetAsync(Guid id,CancellationToken ct){try{var res=await _http.SendAsync(await Request(HttpMethod.Get,"clients/profil",id),ct);return (res.IsSuccessStatusCode,res.IsSuccessStatusCode?await res.Content.ReadFromJsonAsync<ClientProfile>(ct):null);}catch{return(false,null);}}
    public async Task<(bool,string?)> UpdateAsync(Guid id,ClientProfile p,CancellationToken ct){try{var res=await _http.SendAsync(await Request(HttpMethod.Put,"clients/profil",id,new{p.Nom,p.Email,p.NotificationsRdv,p.NotificationsMarketing,p.ConsentementDonnees}),ct);if(res.IsSuccessStatusCode)return(true,null);var e=await res.Content.ReadFromJsonAsync<ApiMessage>(ct);return(false,e?.Message??"Mise à jour impossible.");}catch{return(false,"Service indisponible.");}}
    public async Task<bool> DeleteAsync(Guid id,CancellationToken ct){try{return (await _http.SendAsync(await Request(HttpMethod.Delete,"clients/profil",id),ct)).IsSuccessStatusCode;}catch{return false;}}
    public async Task<(bool Success,string? Message)> RequestPhoneCodeAsync(Guid id,string telephone,CancellationToken ct){try{var res=await _http.SendAsync(await Request(HttpMethod.Post,"clients/profil/telephone/request",id,new{telephone}),ct);if(res.IsSuccessStatusCode)return(true,null);var e=await res.Content.ReadFromJsonAsync<ApiMessage>(ct);return(false,e?.Message??"Envoi impossible.");}catch{return(false,"Service indisponible.");}}
    public async Task<(bool Success,string? Message)> VerifyPhoneAsync(Guid id,string telephone,string code,CancellationToken ct){try{var res=await _http.SendAsync(await Request(HttpMethod.Post,"clients/profil/telephone/verify",id,new{telephone,code}),ct);if(res.IsSuccessStatusCode)return(true,null);var e=await res.Content.ReadFromJsonAsync<ApiMessage>(ct);return(false,e?.Message??"Vérification impossible.");}catch{return(false,"Service indisponible.");}}}
