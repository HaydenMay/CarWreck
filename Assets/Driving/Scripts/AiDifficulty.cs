using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>Player-selectable AI quality. It changes how well the drivers decide, never the cars, the physics, the damage or the scoring.</summary>
    public enum AiDifficulty { Casual, Standard, Competitive }

    /// <summary>
    /// What a difficulty changes about a driver. Every field is a decision-quality or execution knob of <see cref="EnemyDriver"/>; none of them touches a vehicle's
    /// mass, durability, acceleration, top speed, grip or damage, the wreck bonus or the scoring. A driver's personality (<see cref="DriverProfile"/>) stays what it
    /// is: the shifts below move every driver the same way, so a Competitive Brawler is still a Brawler and a Casual Hunter still the sharpest of its field.
    /// </summary>
    public struct DifficultySettings
    {
        /// <summary>Added to the profile's combat intelligence (clamped 0..1): how well attacks are judged and prepared.</summary>
        public float intelligenceShift;
        /// <summary>Added to the profile's driving skill (clamped 0..1): reaction lag, prediction, steering precision. The car itself is untouched.</summary>
        public float skillShift;
        /// <summary>How much of the car's own top speed an intelligent driver uses when it charges (0 = the previous AI's charge speed, 1 = about 93 % of top speed).</summary>
        public float chargeBoost;
        /// <summary>How readily a driver gives up a poor close-range tap to make a run-up instead (scaled by the driver's intelligence).</summary>
        public float setup;
        /// <summary>How often a driver misjudges an attack (scaled by 1 - intelligence): a wider alignment, a weak charge started anyway.</summary>
        public float mistakes;
        /// <summary>How much the scoreboard and the clock change a driver's risk-taking (0 = not at all).</summary>
        public float awareness;
        /// <summary>How strongly target choice weighs attack quality, reachability and wreck chances (the attack-opportunity terms).</summary>
        public float insight;
        /// <summary>How much speed an intelligent driver keeps while it turns and lines up (a car that is crawling when it is hit takes the larger share of the collision energy).</summary>
        public float momentum;
        /// <summary>Multiplies the speed a driver builds up to in a charge (a casual driver does not push the car as hard). Never above 1.</summary>
        public float chargeScale;
        /// <summary>Extra seconds before the driver reacts to what the target does (its view of the target's velocity lags more): a casual driver is slower to read a fight.</summary>
        public float reactionDelay;

        public static DifficultySettings For(AiDifficulty d)
        {
            switch (d)
            {
                case AiDifficulty.Casual:
                    return new DifficultySettings { intelligenceShift = -0.30f, skillShift = -0.15f, reactionDelay = 0.2f, chargeBoost = 0f, chargeScale = 0.92f, setup = 0.4f, mistakes = 1.4f, awareness = 0f, insight = 0.45f, momentum = 0f };
                case AiDifficulty.Competitive:
                    return new DifficultySettings { intelligenceShift = 0.25f, skillShift = 0.10f, reactionDelay = 0f, chargeBoost = 1f, chargeScale = 1f, setup = 1f, mistakes = 0.5f, awareness = 1f, insight = 1f, momentum = 1f };
                default:
                    return new DifficultySettings { intelligenceShift = 0f, skillShift = 0f, reactionDelay = 0f, chargeBoost = 0.35f, chargeScale = 1f, setup = 0.8f, mistakes = 1f, awareness = 0.5f, insight = 0.75f, momentum = 0.5f };
            }
        }

        public static string Label(AiDifficulty d) => d == AiDifficulty.Casual ? "CASUAL" : d == AiDifficulty.Competitive ? "COMPETITIVE" : "STANDARD";
    }
}
