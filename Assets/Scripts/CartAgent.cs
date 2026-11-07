using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CartAgent : Agent
{
    [Header("Hivatkozások")]
    public Transform target;               // ide kell behúzni a Target objektumot

    [Header("Mozgás")]
    public float pushAcceleration = 8f;
    public float strafeAcceleration = 4f;
    public float turnSpeed = 150f;

    [Header("Jutalmak")]
    public float targetReward = 1f;
    public float progressRewardPerMeter = 0.01f;
    public float collisionPenalty = -0.1f;
    public float scrapePenalty = -0.005f;

    // A polcsorok x-pozíciói (a StoreArea-hoz képest)
    readonly float[] shelfX = { -9f, -3f, 3f, 9f };
    // A folyosók közepének x-pozíciói (a polcok között és a falak mellett)
    readonly float[] aisleX = { -12.25f, -6f, 0f, 6f, 12.25f };
    const float targetSideOffset = 1.35f;  // polc fél-szélessége + cél fél-szélessége + kis rés

    Rigidbody rb;
    float prevDistance;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        // 1) Kocsi: véletlen folyosó, véletlen hely, véletlen irány
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        float ax = aisleX[Random.Range(0, aisleX.Length)] + Random.Range(-1f, 1f);
        transform.localPosition = new Vector3(ax, 0f, Random.Range(-12f, 12f));
        transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // 2) Cél: véletlen polc mellé, de ne közvetlenül a kocsi mellé
        int tries = 0;
        do
        {
            float sx = shelfX[Random.Range(0, shelfX.Length)];
            float side = Random.value < 0.5f ? -1f : 1f;
            target.localPosition = new Vector3(sx + side * targetSideOffset, 0.5f, Random.Range(-6f, 6f));
            tries++;
        } while (DistanceToTarget() < 4f && tries < 20);

        prevDistance = DistanceToTarget();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Saját sebesség a kocsi szemszögéből (osztva, hogy kb. -1..1 közé essen)
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVel.x / 5f);
        sensor.AddObservation(localVel.z / 5f);

        // Merre van a cél a kocsihoz képest (egységvektor), és milyen messze
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        Vector3 localDir = transform.InverseTransformDirection(toTarget.normalized);
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);
        sensor.AddObservation(toTarget.magnitude / 40f);
        // Összesen 5 szám. A "lézersugarakat" külön komponens adja hozzá.
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Mozgás ---
        float push = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float steer = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float strafe = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);

        Vector3 force = transform.forward * push * pushAcceleration
                      + transform.right * strafe * strafeAcceleration;
        rb.AddForce(force, ForceMode.Acceleration);

        Quaternion turn = Quaternion.Euler(0f, steer * turnSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);

        // --- Jutalmak ---
        float d = DistanceToTarget();
        AddReward((prevDistance - d) * progressRewardPerMeter);  // közeledés
        prevDistance = d;

        if (MaxStep > 0)
            AddReward(-1f / MaxStep);                            // időbüntetés
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var a = actionsOut.ContinuousActions;
        a[0] = 0f; a[1] = 0f; a[2] = 0f;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.wKey.isPressed) a[0] += 1f;
        if (kb.sKey.isPressed) a[0] -= 1f;
        if (kb.dKey.isPressed) a[1] += 1f;
        if (kb.aKey.isPressed) a[1] -= 1f;
        if (kb.eKey.isPressed) a[2] += 1f;
        if (kb.qKey.isPressed) a[2] -= 1f;
    }

    float DistanceToTarget()
    {
        Vector3 diff = target.position - transform.position;
        diff.y = 0f;
        return diff.magnitude;
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.collider.CompareTag("Wall") || c.collider.CompareTag("Shelf"))
            AddReward(collisionPenalty);
    }

    void OnCollisionStay(Collision c)
    {
        if (c.collider.CompareTag("Wall") || c.collider.CompareTag("Shelf"))
            AddReward(scrapePenalty);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            AddReward(targetReward);
            EndEpisode();
        }
    }
}