using System;

public class Health
{
    public float MaxHealth { get; private set; }
    public float CurrentHealth { get; private set; }

    // Events for external systems (UI, effects, etc.)
    public event Action<float, float> OnHealthChanged; // current, max
    public event Action<float> OnDamaged;              // damage amount
    public event Action<float> OnHealed;               // heal amount
    public event Action OnDeath;

    public bool IsAlive => CurrentHealth > 0f;

    public Health(float maxHealth, float? oldCurrentHealth = null)
    {
        if (maxHealth <= 0f)
            throw new ArgumentException("Max health must be greater than zero.", nameof(maxHealth));

        MaxHealth = maxHealth;

        // Use old value if provided, otherwise start full
        CurrentHealth = oldCurrentHealth ?? MaxHealth;

        // Clamp so it never exceeds max
        CurrentHealth = Math.Clamp(CurrentHealth, 0f, MaxHealth);

        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }


    public void SetMaxHealth(float newMax, bool clampCurrent = true)
    {
        if (newMax <= 0f)
            throw new ArgumentException("Max health must be greater than zero.", nameof(newMax));

        MaxHealth = newMax;
        if (clampCurrent)
            CurrentHealth = Math.Min(CurrentHealth, MaxHealth);

        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    public void Damage(float amount)
    {
        if (!IsAlive || amount <= 0f) return;

        CurrentHealth = Math.Max(0f, CurrentHealth - amount);
        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0f)
            OnDeath?.Invoke();
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f) return;

        float prev = CurrentHealth;
        CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);

        float healed = CurrentHealth - prev;

        if (healed > 0f)
        {
            OnHealed?.Invoke(healed);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }

    public void Kill()
    {
        if (!IsAlive) return;

        CurrentHealth = 0f;
        OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        OnDeath?.Invoke();
    }
}
