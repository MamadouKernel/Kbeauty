namespace KekeBeauty.Web.Models;
public sealed class ClientProfile { public Guid IdUtilisateur {get;set;} public string Telephone {get;set;}=""; public string Nom {get;set;}=""; public string? Email {get;set;} public bool NotificationsRdv {get;set;} public bool NotificationsMarketing {get;set;} public bool ConsentementDonnees {get;set;} public int PointsFidelite {get;set;} }
public sealed class ApiMessage { public string? Message {get;set;} }
