using UnityEngine;
using UnityEngine.Serialization;

namespace ElderTree.Player
{
    /// <summary>HP / Mana / Stamina with Elden Ring style stamina rules: any positive stamina lets an
    /// action start (it may go negative). Nothing regenerates while an action is under way (<see cref="RegenHeld"/>),
    /// and regeneration waits <see cref="staminaRegenDelay"/> after the action ends.
    /// Mana pays for the ranged weapons and spells: it refills on its own over time, but
    /// every spend stops the clock for <see cref="manaRegenDelay"/> seconds first.
    /// Special attacks are paid with charges instead: light and heavy hits on an enemy fill them, each
    /// special spends one, and a boss fight starts empty and allows at most <see cref="specialsPerBoss"/>.</summary>
    public class PlayerStats : MonoBehaviour
    {
        public float maxHealth = 620f;
        [FormerlySerializedAs("maxFocus")] public float maxMana = 120f;
        public float maxStamina = 110f;
        [Tooltip("Elden Ring base regeneration is ~45/s.")]
        public float staminaRegen = 45f;
        public float staminaRegenGuarding = 18f;
        public float staminaRegenDelay = 0.55f;

        [Header("Exhaustion (set per form, see FormData)")]
        [Tooltip("Running the bar dry exhausts the body: a long pause before regeneration, and no attacks until it recovers.")]
        public bool exhaustionEnabled;
        [Tooltip("Seconds after the bar runs dry before regeneration starts.")]
        public float exhaustedRegenDelay = 1f;
        [Tooltip("Attacks stay locked until stamina is back up to this much.")]
        public float exhaustedRecoverTo = 30f;
        [Tooltip("Passive mana regeneration per second.")]
        [FormerlySerializedAs("focusRegen")] public float manaRegen = 5.5f;
        [Tooltip("After mana is spent, regeneration stops for this long before it resumes.")]
        public float manaRegenDelay = 2f;

        [Header("Special charges")]
        [Tooltip("Charges that can be banked at once. Each special attack spends one.")]
        public int maxSpecialCharges = 2;
        [Tooltip("Specials allowed per boss fight: once this many are spent, hits stop filling charges until the fight ends.")]
        public int specialsPerBoss = 2;
        [Tooltip("Charge gained per light-attack hit (0.05 = 20 hits per charge, about a quarter of a boss's health).")]
        public float chargePerLightHit = 0.05f;
        [Tooltip("Charge gained per heavy-attack hit (0.1 = 10 hits per charge; heavies deal about twice a light's damage).")]
        public float chargePerHeavyHit = 0.1f;
        [Tooltip("Charge per light spell (Vine Seeker) hit: a cast deals about a heavy swing's damage.")]
        public float chargePerSpellHit = 0.1f;
        [Tooltip("Charge per charged spell hit.")]
        public float chargePerChargedSpellHit = 0.15f;

        [Header("Health potions")]
        [Tooltip("Potions carried. Dying or resting refills them all.")]
        public int maxPotions = 5;
        [Tooltip("Health one potion restores (620 max HP: ~42%).")]
        public float potionHeal = 260f;

        public float Health { get; private set; }
        public int Potions { get; private set; }
        public float Mana { get; private set; }
        public float Stamina { get; private set; }
        /// <summary>Special charge, 0..<see cref="maxSpecialCharges"/>; the whole part is what can be spent.</summary>
        public float SpecialCharge { get; private set; }
        public int SpecialChargesReady => Mathf.FloorToInt(SpecialCharge + 0.001f);
        /// <summary>True during a boss fight (specials there are capped at <see cref="specialsPerBoss"/>).</summary>
        public bool InBossFight { get; private set; }
        public int SpecialsUsedThisFight { get; private set; }
        /// <summary>How far the charge can fill right now: the bank size, or what the boss fight still allows.</summary>
        public float SpecialChargeCap => InBossFight
            ? Mathf.Clamp(specialsPerBoss - SpecialsUsedThisFight, 0, maxSpecialCharges)
            : maxSpecialCharges;
        public bool Guarding { get; set; }
        /// <summary>Set by the controller every frame while the body is busy (attacking, rolling, grabbed, knocked
        /// down): stamina doesn't regenerate, and the regen delay restarts when the action ends.</summary>
        public bool RegenHeld { get; set; }
        /// <summary>True while the post-spend pause holds mana regeneration.</summary>
        public bool ManaRegenPaused => Time.time - lastManaSpend < manaRegenDelay;

        /// <summary>The bar ran dry (exhaustion forms only): attacks are refused until it recovers.</summary>
        public bool Exhausted { get; private set; }
        public bool CanAttack => !Exhausted;

        float lastSpend = -99, lastManaSpend = -99, exhaustedAt = -99;

        public event System.Action Changed;
        /// <summary>An attack was refused because the body is exhausted (HUD blinks the stamina bar).</summary>
        public event System.Action StaminaDenied;
        /// <summary>A spend failed for lack of mana (HUD flashes the bar).</summary>
        public event System.Action ManaDenied;
        /// <summary>A special was refused for lack of a charge (HUD shakes the charge slots).</summary>
        public event System.Action SpecialDenied;
        /// <summary>A charge slot just filled (HUD flashes it).</summary>
        public event System.Action SpecialChargeReady;
        /// <summary>A potion was uncorked (HUD pulses the flask slot).</summary>
        public event System.Action PotionUsed;
        /// <summary>Tried to drink with none left (HUD shakes the flask slot).</summary>
        public event System.Action PotionDenied;

        void Awake()
        {
            Health = maxHealth;
            Potions = maxPotions;
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
            CheckExhaustion();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Pay an attack's stamina: refused outright while exhausted (the punishment for running dry).</summary>
        public bool TrySpendAttackStamina(float amount)
        {
            if (Exhausted)
            {
                StaminaDenied?.Invoke();
                return false;
            }
            return TrySpendStamina(amount);
        }

        public void DrainStamina(float amountPerSecond, float dt)
        {
            Stamina = Mathf.Max(-5f, Stamina - amountPerSecond * dt);
            lastSpend = Time.time;
            CheckExhaustion();
        }

        /// <summary>Hitting zero (again) starts / restarts the exhaustion pause.</summary>
        void CheckExhaustion()
        {
            if (!exhaustionEnabled || Stamina > 0f) return;
            exhaustedAt = Time.time;
            Exhausted = true;
        }

        /// <summary>The form changed: take its exhaustion rules (turning them off clears any exhaustion).</summary>
        public void SetExhaustion(bool enabled, float regenDelay, float recoverTo)
        {
            exhaustionEnabled = enabled;
            exhaustedRegenDelay = regenDelay;
            exhaustedRecoverTo = recoverTo;
            if (!enabled && Exhausted) { Exhausted = false; Changed?.Invoke(); }
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

        public bool HasSpecialCharge => SpecialCharge >= 1f - 0.001f;

        /// <summary>Fill the special charge from a landed light / heavy hit (see the chargePer* fields).</summary>
        public void GainSpecialCharge(float amount)
        {
            float cap = SpecialChargeCap;
            if (amount <= 0f || SpecialCharge >= cap) return;
            int before = SpecialChargesReady;
            SpecialCharge = Mathf.Min(cap, SpecialCharge + amount);
            if (SpecialChargesReady > before) SpecialChargeReady?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>Spend one charge on a special; false (and the HUD is told) when none is ready.</summary>
        public bool TrySpendSpecialCharge()
        {
            if (!HasSpecialCharge)
            {
                SpecialDenied?.Invoke();
                return false;
            }
            SpecialCharge = Mathf.Max(0f, SpecialCharge - 1f);
            if (InBossFight) SpecialsUsedThisFight++;
            SpecialCharge = Mathf.Min(SpecialCharge, SpecialChargeCap);
            Changed?.Invoke();
            return true;
        }

        /// <summary>A boss fight begins: charges start empty, and only <see cref="specialsPerBoss"/> can be used.</summary>
        public void BeginBossFight()
        {
            InBossFight = true;
            SpecialsUsedThisFight = 0;
            SpecialCharge = 0f;
            Changed?.Invoke();
        }

        /// <summary>The boss fight is over (won, lost or dismissed): the per-fight cap lifts.</summary>
        public void EndBossFight()
        {
            InBossFight = false;
            SpecialsUsedThisFight = 0;
            Changed?.Invoke();
        }

        /// <summary>Debug: set the charge directly (clamped to the current cap).</summary>
        public void DevSetSpecialCharge(float value)
        {
            SpecialCharge = Mathf.Clamp(value, 0f, SpecialChargeCap);
            Changed?.Invoke();
        }

        public void TakeDamage(float dmg)
        {
            Health = Mathf.Max(0f, Health - dmg);
            Changed?.Invoke();
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            Health = Mathf.Min(maxHealth, Health + amount);
            Changed?.Invoke();
        }

        /// <summary>Spend one potion (the heal itself lands later, at the gulp). False when none are left.</summary>
        public bool TryUsePotion()
        {
            if (Potions <= 0)
            {
                PotionDenied?.Invoke();
                return false;
            }
            Potions--;
            PotionUsed?.Invoke();
            Changed?.Invoke();
            return true;
        }

        public void RefillPotions()
        {
            Potions = maxPotions;
            Changed?.Invoke();
        }

        public void RestoreAll()
        {
            Health = maxHealth; Mana = maxMana; Stamina = maxStamina; SpecialCharge = 0f;
            Potions = maxPotions;
            Exhausted = false;
            Changed?.Invoke();
        }

        void Update()
        {
            // mid-action (swinging, rolling, grabbed, knocked down): no regeneration, and both the regen
            // delay and the exhaustion pause only start counting once the action is over
            if (RegenHeld)
            {
                lastSpend = Mathf.Max(lastSpend, Time.time);
                if (Exhausted) exhaustedAt = Time.time;
            }
            bool exhaustPause = Exhausted && Time.time - exhaustedAt < exhaustedRegenDelay;
            if (!exhaustPause && Time.time - lastSpend > staminaRegenDelay && Stamina < maxStamina)
                Stamina = Mathf.Min(maxStamina, Stamina + (Guarding ? staminaRegenGuarding : staminaRegen) * Time.deltaTime);
            if (Exhausted && !exhaustPause && Stamina >= Mathf.Min(exhaustedRecoverTo, maxStamina) - 0.01f)
            {
                Exhausted = false;
                Changed?.Invoke();
            }
            if (Mana < maxMana && !ManaRegenPaused)
                Mana = Mathf.Min(maxMana, Mana + manaRegen * Time.deltaTime);
        }
    }
}
