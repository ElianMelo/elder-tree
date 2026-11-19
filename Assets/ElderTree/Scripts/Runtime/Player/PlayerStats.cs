using UnityEngine;
using UnityEngine.Serialization;

namespace ElderTree.Player
{
    /// <summary>HP / Mana / Stamina with Elden Ring style stamina rules: any positive stamina lets an
    /// action start (it may go negative), regeneration waits for a short delay after spending.
    /// Mana pays for the ranged weapons and special attacks: it refills on its own over time, but
    /// every spend stops the clock for <see cref="manaRegenDelay"/> seconds first.</summary>
    public class PlayerStats : MonoBehaviour
    {
        public float maxHealth = 620f;
        [FormerlySerializedAs("maxFocus")] public float maxMana = 120f;
        public float maxStamina = 110f;
        [Tooltip("Elden Ring base regeneration is ~45/s.")]
        public float staminaRegen = 45f;
        public float staminaRegenGuarding = 18f;
        public float staminaRegenDelay = 0.55f;
        [Tooltip("Passive mana regeneration per second.")]
        [FormerlySerializedAs("focusRegen")] public float manaRegen = 11f;
        [Tooltip("After mana is spent, regeneration stops for this long before it resumes.")]
        public float manaRegenDelay = 2f;

        public float Health { get; private set; }
        public float Mana { get; private set; }
        public float Stamina { get; private set; }
        public bool Guarding { get; set; }
        /// <summary>True while the post-spend pause holds mana regeneration.</summary>
        public bool ManaRegenPaused => Time.time - lastManaSpend < manaRegenDelay;

        float lastSpend = -99, lastManaSpend = -99;

        public event System.Action Changed;
        /// <summary>A spend failed for lack of mana (HUD flashes the bar).</summary>
        public event System.Action ManaDenied;

        void Awake()
        {
            Health = maxHealth;
            Mana = maxMana;
            Stamina = maxStamina;
        }

        public bool HasStamina => Stamina > 0.01f;

        public bool TrySpendStamina(float amount)
        {
            if (!HasStamina) return false;
            Stamina -= amount;
            if (Stamina < -15f) Stamina = -15f;
            lastSpend = Time.time;
            Changed?.Invoke();
            return true;
        }

        public void DrainStamina(float amountPerSecond, float dt)
        {
            Stamina = Mathf.Max(-5f, Stamina - amountPerSecond * dt);
            lastSpend = Time.time;
        }

        public bool HasMana(float amount) => Mana >= amount - 0.001f;

        public bool TrySpendMana(float amount)
        {
            if (amount <= 0f) return true;
            if (!HasMana(amount))
            {
                ManaDenied?.Invoke();
                return false;
            }
            Mana = Mathf.Max(0f, Mana - amount);
            lastManaSpend = Time.time;
            Changed?.Invoke();
            return true;
        }

        public void TakeDamage(float dmg)
        {
            Health = Mathf.Max(0f, Health - dmg);
            Changed?.Invoke();
        }

        public void RestoreAll()
        {
            Health = maxHealth; Mana = maxMana; Stamina = maxStamina;
            Changed?.Invoke();
        }

        void Update()
        {
            if (Time.time - lastSpend > staminaRegenDelay && Stamina < maxStamina)
                Stamina = Mathf.Min(maxStamina, Stamina + (Guarding ? staminaRegenGuarding : staminaRegen) * Time.deltaTime);
            if (Mana < maxMana && !ManaRegenPaused)
                Mana = Mathf.Min(maxMana, Mana + manaRegen * Time.deltaTime);
        }
    }
}
