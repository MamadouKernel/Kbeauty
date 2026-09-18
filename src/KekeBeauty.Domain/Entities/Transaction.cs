namespace KekeBeauty.Domain.Entities;

public enum CanalPaiement
{
    Wave,
    OrangeMoney,
    Mtn,
    Moov,
    Visa,
    Mastercard,
}

public enum StatutTransaction
{
    EnCours,
    Reussie,
    Echouee,
    Remboursee,
}

public class Transaction
{
    public Guid IdTransaction { get; set; }
    public CanalPaiement CanalPaiement { get; set; }
    public StatutTransaction StatutTransaction { get; set; }
    public DateTimeOffset DateTransaction { get; set; }
    public decimal Montant { get; set; }
    public Guid IdAbonnement { get; set; }
}
