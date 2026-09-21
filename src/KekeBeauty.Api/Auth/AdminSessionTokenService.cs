using System.Security.Cryptography;
using System.Text;
namespace KekeBeauty.Api.Auth;
public sealed record AdminIdentity(Guid Id,string Name,string Role);
public sealed class AdminSessionTokenService {
 private readonly byte[] _key;
 public AdminSessionTokenService(IConfiguration c){var value=c["Admin:SigningKey"];if(string.IsNullOrWhiteSpace(value))value=c["Admin:BootstrapPassword"]??c["Admin:ApiKey"];if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("Admin:SigningKey doit etre configuree.");_key=SHA256.HashData(Encoding.UTF8.GetBytes(value));}
 public string Create(AdminIdentity a){var exp=DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();var payload=$"{a.Id:N}|{exp}|{a.Role}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(a.Name))}";var sig=Convert.ToBase64String(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(payload)));return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload+"|"+sig));}
 public bool TryValidate(string? token,out AdminIdentity? identity){identity=null;try{var all=Encoding.UTF8.GetString(Convert.FromBase64String(token??""));var p=all.Split('|');if(p.Length!=5||!Guid.TryParseExact(p[0],"N",out var id)||!long.TryParse(p[1],out var exp)||exp<DateTimeOffset.UtcNow.ToUnixTimeSeconds())return false;var payload=string.Join('|',p.Take(4));if(!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(payload)),Convert.FromBase64String(p[4])))return false;identity=new(id,Encoding.UTF8.GetString(Convert.FromBase64String(p[3])),p[2]);return true;}catch{return false;}}
}
