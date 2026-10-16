using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// A driver personality for <see cref="EnemyDriver"/>: two independent numbers, nothing else. The profile changes WHAT the driver decides
    /// (aggression) and how well it EXECUTES it (skill); it never touches the vehicle, its handling or its physics, and it is independent of the car:
    /// the same profile can drive the pickup, the hatchback or the muscle car, and the same car can be driven by any profile.
    /// All the numbers the AI derives from these two (alignment tolerance, runway, reaction time, ...) are in <see cref="EnemyDriver"/>'s tuning block.
    /// </summary>
    [CreateAssetMenu(menuName = "CarWreck/Driver Profile", fileName = "DriverProfile")]
    public class DriverProfile : ScriptableObject
    {
        public string displayName = "Driver";
        [TextArea(1, 3)] public string description;
        [Tooltip("How eagerly the driver seeks a collision and how much risk it accepts. 0 = picks only favourable attacks, keeps its distance (still engages), 1 = reckless: attacks at once from any angle.")]
        [Range(0f, 1f)] public float aggression = 0.5f;
        [Tooltip("How well the driver executes what it decided. 0 = loose alignment, late braking, overshoots, poor prediction; 1 = precise approach, good short-range prediction, quick correction. Recovery from obstacles is the same for every skill.")]
        [Range(0f, 1f)] public float skill = 0.5f;

        public const int PresetCount = 3;

        /// <summary>The three starting profiles (also written to assets by the scene builder). 0 = Brawler, 1 = Hunter, 2 = Cautious.</summary>
        public static DriverProfile CreatePreset(int index)
        {
            var p = CreateInstance<DriverProfile>();
            switch (index)
            {
                case 0:
                    p.name = p.displayName = "Brawler"; p.aggression = 0.85f; p.skill = 0.5f;
                    p.description = "Seeks collisions constantly and takes imperfect chances. Rarely backs off.";
                    break;
                case 1:
                    p.name = p.displayName = "Hunter"; p.aggression = 0.65f; p.skill = 0.9f;
                    p.description = "Reads where you are going, lines up properly and hits hard. Backs off to build speed when it is too close.";
                    break;
                default:
                    p.name = p.displayName = "Cautious"; p.aggression = 0.25f; p.skill = 0.5f;
                    p.description = "Waits for a good angle and keeps its distance, but still comes in regularly.";
                    break;
            }
            return p;
        }
    }
}
