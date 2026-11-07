using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ShopperNPC : MonoBehaviour
{
    [Header("Séta")]
    public float minSpeed = 0.8f;          // m/s
    public float maxSpeed = 1.4f;

    [Header("Megállás (nézelődés a polcnál)")]
    public float stopChancePerSecond = 0.15f;
    public float minStopTime = 2f;
    public float maxStopTime = 5f;

    // A folyosók közepei (ugyanazok, mint a CartAgent-ben)
    static readonly float[] aisleX = { -12.25f, -6f, 0f, 6f, 12.25f };
    const float zLimit = 11f;

    Rigidbody rb;
    Vector3 localPos;
    float speed;
    float dir;
    float stopTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // A kocsi hívja meg minden epizód elején
    public void ResetShopper(Vector3 cartLocalPos)
    {
        int tries = 0;
        do
        {
            // Véletlen folyosó, és azon belül nem mindig középen
            float x = aisleX[Random.Range(0, aisleX.Length)] + Random.Range(-0.8f, 0.8f);
            localPos = new Vector3(x, 0f, Random.Range(-zLimit, zLimit));
            tries++;
        }
        while (FlatDistance(localPos, cartLocalPos) < 4f && tries < 20);

        transform.localPosition = localPos;
        speed = Random.Range(minSpeed, maxSpeed);
        dir = Random.value < 0.5f ? -1f : 1f;
        stopTimer = 0f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Éppen nézelődik: áll
        if (stopTimer > 0f)
        {
            stopTimer -= dt;
            return;
        }

        // Véletlenszerűen megáll
        if (Random.value < stopChancePerSecond * dt)
        {
            stopTimer = Random.Range(minStopTime, maxStopTime);
            return;
        }

        // Sétál a folyosón, a végén megfordul
        localPos.z += dir * speed * dt;
        if (localPos.z > zLimit) { localPos.z = zLimit; dir = -1f; }
        else if (localPos.z < -zLimit) { localPos.z = -zLimit; dir = 1f; }

        rb.MovePosition(transform.parent.TransformPoint(localPos));
        rb.MoveRotation(transform.parent.rotation * Quaternion.Euler(0f, dir > 0f ? 0f : 180f, 0f));
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}