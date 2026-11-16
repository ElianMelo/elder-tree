using UnityEngine;

namespace TungSahur.Player
{
    /// <summary>HP / FP / Stamina with Elden Ring style stamina rules: any positive stamina lets an
    /// action start (it may go negative), regeneration waits for a short delay after spending.</summary>
    public class PlayerStats : MonoBehaviour
    {
        public float maxHealth = 620f;
        public float maxFocus = 120f;
        public float maxStamina = 110f;
        [Tooltip("Elden Ring base regeneration is ~45/s.")]
        public float staminaRegen = 45f;
        public float staminaRegenGuarding = 18f;
        public float staminaRegenDelay = 0.55f;
        public float focusRegen = 4f;

        public float Health { get; private set; }
        public float Focus { get; private set; }
        public float Stamina { get; private set; }
        public bool Guarding { get; set; }

        float lastSpend = -99;

        public event System.Action Changed;

        void Awake()
        {
            Health = maxHealth;
            Focus = maxFocus;
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

        public bool TrySpendFocus(float amount)
        {
            if (Focus < amount) return false;
            Focus -= amount;
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
            Health = maxHealth; Focus = maxFocus; Stamina = maxStamina;
            Changed?.Invoke();
        }

        void Update()
        {
            if (Time.time - lastSpend > staminaRegenDelay && Stamina < maxStamina)
                Stamina = Mathf.Min(maxStamina, Stamina + (Guarding ? staminaRegenGuarding : staminaRegen) * Time.deltaTime);
            if (Focus < maxFocus)
                Focus = Mathf.Min(maxFocus, Focus + focusRegen * Time.deltaTime);
        }
    }
}
