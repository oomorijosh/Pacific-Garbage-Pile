using UnityEngine;

/// <summary>Kinematic surface following for lightweight debris, not a fluid solver.</summary>
[DisallowMultipleComponent]
public sealed class FloatingDebris : MonoBehaviour
{
    public OceanSurface ocean;
    public float waterlineOffset;
    [Min(0)] public float currentMultiplier = 1;
    [Min(0)] public float alignmentSpeed = 4;
    Rigidbody body;
    bool previousKinematic;

    void OnEnable()
    {
        if (!ocean) ocean = FindAnyObjectByType<OceanSurface>();
        body = GetComponent<Rigidbody>();
        if (body) { previousKinematic = body.isKinematic; body.isKinematic = true; }
    }
    void FixedUpdate()
    {
        if (!ocean) return;
        Vector3 position = body ? body.position : transform.position;
        position += ocean.Current(position) * (currentMultiplier * Time.fixedDeltaTime);
        ocean.Sample(position, Time.time, out Vector3 surface, out Vector3 normal);
        surface += Vector3.up * waterlineOffset;
        Quaternion rotation = body ? body.rotation : transform.rotation;
        Quaternion target = Quaternion.FromToRotation(rotation * Vector3.up, normal) * rotation;
        rotation = Quaternion.Slerp(rotation, target, 1 - Mathf.Exp(-alignmentSpeed * Time.fixedDeltaTime));
        if (body) { body.MovePosition(surface); body.MoveRotation(rotation); }
        else transform.SetPositionAndRotation(surface, rotation);
    }
    void OnDisable() { if (body) body.isKinematic = previousKinematic; }
}
