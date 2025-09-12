using UnityEngine;
using System;

public class PlayerStats : MonoBehaviour
{
    [Range(0, 100)] public int maxHealth = 100;
    public int Health { get; private set; }

    public int AmmoInMag { get; private set; } = 12;
    public int AmmoReserve { get; private set; } = 48;
    public string WeaponName { get; private set; } = "Pistol";

    public event Action<int, int> OnHealthChanged;          // current, max
    public event Action<int, int> OnAmmoChanged;            // mag, reserve
    public event Action<string> OnWeaponChanged;         // name

    void Awake() => Health = maxHealth;

    public void TakeDamage(int amount)
    {
        Health = Mathf.Clamp(Health - amount, 0, maxHealth);
        OnHealthChanged?.Invoke(Health, maxHealth);
    }

    public void Heal(int amount)
    {
        Health = Mathf.Clamp(Health + amount, 0, maxHealth);
        OnHealthChanged?.Invoke(Health, maxHealth);
    }

    public void SetWeapon(string name, int mag, int reserve)
    {
        WeaponName = name;
        AmmoInMag = mag;
        AmmoReserve = reserve;
        OnWeaponChanged?.Invoke(WeaponName);
        OnAmmoChanged?.Invoke(AmmoInMag, AmmoReserve);
    }

    public bool TryShoot()
    {
        if (AmmoInMag <= 0) return false;
        AmmoInMag--;
        OnAmmoChanged?.Invoke(AmmoInMag, AmmoReserve);
        return true;
    }

    public bool TryReload(int magSize)
    {
        int needed = magSize - AmmoInMag;
        if (needed <= 0 || AmmoReserve <= 0) return false;

        int toLoad = Mathf.Min(needed, AmmoReserve);
        AmmoInMag += toLoad;
        AmmoReserve -= toLoad;
        OnAmmoChanged?.Invoke(AmmoInMag, AmmoReserve);
        return true;
    }
}
