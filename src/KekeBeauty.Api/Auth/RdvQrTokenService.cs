using System.Security.Cryptography;
using System.Text;
namespace KekeBeauty.Api.Auth;
public sealed class RdvQrTokenService(IConfiguration config)
{
 readonly byte[] _key=SHA256.HashData(Encoding.UTF8.GetBytes(config["Authentication:SessionSigningKey"]??throw new InvalidOperationException("Clé de signature absente.")));
 public string Create(Guid rdv,Guid client){var payload=$"{rdv:N}|{client:N}|{DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()}";var data=Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+','-').Replace('/','_');var sig=Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(data)));return $"{data}.{sig}";}
 public bool TryValidate(string? token,out Guid rdv,out Guid client){rdv=client=Guid.Empty;if(string.IsNullOrWhiteSpace(token))return false;var p=token.Split('.');if(p.Length!=2)return false;var expected=Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(p[0])));if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(p[1])))return false;try{var b=p[0].Replace('-','+').Replace('_','/');b=b.PadRight(b.Length+(4-b.Length%4)%4,'=');var v=Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('|');return v.Length==3&&Guid.TryParseExact(v[0],"N",out rdv)&&Guid.TryParseExact(v[1],"N",out client)&&long.TryParse(v[2],out var exp)&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()<exp;}catch{return false;}}
}