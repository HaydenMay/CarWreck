namespace DamageLab.Deformation
{
    /// <summary>The four impact directions the lab can apply. Values double as array indices.</summary>
    public enum DamageDirection
    {
        Front = 0,
        Rear = 1,
        Left = 2,
        Right = 3,
    }

    public static class DamageDirections
    {
        public const int Count = 4;

        public static readonly DamageDirection[] All =
        {
            DamageDirection.Front, DamageDirection.Rear, DamageDirection.Left, DamageDirection.Right,
        };
    }
}
