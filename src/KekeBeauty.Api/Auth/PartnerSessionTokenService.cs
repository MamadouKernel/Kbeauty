using System.Security.Cryptography;
using System.Text;
namespace KekeBeauty.Api.Auth;
public sealed class PartnerSessionTokenService(IConfiguration config)
{
 readonly byte[] _key=ResolveKey(config);
 static byte[] ResolveKey(IConfiguration config){var configured=config["Authentication:SessionSigningKey"]??config["Admin:SigningKey"]??config["Admin:BootstrapPassword"];if(string.IsNullOrWhiteSpace(configured)||Encoding.UTF8.GetByteCount(configured)<32)throw new InvalidOperationException("Authentication:SessionSigningKey doit contenir au moins 32 octets.");return SHA256.HashData(Encoding.UTF8.GetBytes(configured));}
 public string Create(Guid id){var payload=$"{id:N}|{DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()}";var data=Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+','-').Replace('/','_');var sig=Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(data)));return $"{data}.{sig}";}
 public bool TryValidate(string? token,out Guid id){id=Guid.Empty;if(string.IsNullOrWhiteSpace(token))return false;var parts=token.Split('.');if(parts.Length!=2)return false;var expected=Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(parts[0])));if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(parts[1])))return false;try{var b=parts[0].Replace('-','+').Replace('_','/');b=b.PadRight(b.Length+(4-b.Length%4)%4,'=');var values=Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('|');return values.Length==2&&Guid.TryParseExact(values[0],"N",out id)&&long.TryParse(values[1],out var exp)&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()<exp;}catch{return false;}}
 public bool TryFromRequest(HttpRequest request,out Guid id){id=Guid.Empty;var values=request.Headers["X-Partner-Token"];return values.Count==1&&TryValidate(values[0],out id);}
}
