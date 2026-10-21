using UnityEngine;

namespace DamageLab.Vehicle
{
    /// <summary>
    /// Marks a node under a vehicle whose meshes are NOT body parts (mounted attachments). <see cref="VehicleDamageModel.Initialize"/> skips every MeshFilter below it,
    /// so an item is never discovered as a deformable or rigid part, whether it is mounted before Initialize, after it, or a Rebuild runs with items on the car.
    /// Inert when absent: vehicles without it behave exactly as before.
    /// </summary>
    public class AttachmentRoot : MonoBehaviour { }
}
