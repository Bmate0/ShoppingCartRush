using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CartAgent : Agent
{
    [Header("Hivatkozások")]
    public Transform target;
    public ShopperNPC[] shoppers;                 // ÚJ: a bolt vásárlói

    [Header("Mozgás")]
    public float pushAcceleration = 8f;
    public float strafeAcceleration = 4f;
    public float turnSpeed = 150f;

    [Header("Jutalmak")]
    public float targetReward = 1f;
    public float progressRewardPerMeter = 0.05f;
    public float collisionPenalty = -0.05f;
    public float scrapePenalty = -0.001f;
    public float shopperHitPenalty = -0.5f;       // ÚJ: vásárló elütése

    readonly float[] shelfX = { -9f, -3f, 3f, 9f };
    readonly float[] aisleX = { -12.25f, -6f, 0f, 6f, 12.25f };
    const float targetSideOffset = 1.35f;

    Rigidbody rb;
    float prevDistance;

    // ÚJ: statisztikák a TensorBoardhoz
    bool reachedTarget;
    bool hitShopper;
    int episodeCount;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        // ÚJ: az előző epizód eredményének naplózása (sikerességi arány)
        if (episodeCount > 0)
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Shop/SuccessRate", reachedTarget ? 1f : 0f);
            stats.Add("Shop/ShopperHitRate", hitShopper ? 1f : 0f);
        }
        episodeCount++;
        reachedTarget = false;
        hitShopper = false;

        // Kocsi: véletlen folyosó, hely, irány
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        float ax = aisleX[Random.Range(0, aisleX.Length)] + Random.Range(-1f, 1f);
        transform.localPosition = new Vector3(ax, 0f, Random.Range(-12f, 12f));
        transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Cél: véletlen polc mellé, ne közvetlenül a kocsi mellé
        int tries = 0;
        do
        {
            float sx = shelfX[Random.Range(0, shelfX.Length)];
            float side = Random.value < 0.5f ? -1f : 1f;
            target.localPosition = new Vector3(sx + side * targetSideOffset, 0.5f, Random.Range(-6f, 6f));
            tries++;
        } while (DistanceToTarget() < 4f && tries < 20);

        // ÚJ: vásárlók. Számukat a tanító állíthatja (curriculum), alapból mind aktív
        int active = Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("shopper_count", shoppers.Length));
        for (int i = 0; i < shoppers.Length; i++)
        {
            bool on = i < active;
            shoppers[i].gameObject.SetActive(on);
            if (on) shoppers[i].ResetShopper(transform.localPosition);
        }

        prevDistance = DistanceToTarget();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVel.x / 5f);
        sensor.AddObservation(localVel.z / 5f);

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        Vector3 localDir = transform.InverseTransformDirection(toTarget.normalized);
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);
        sensor.AddObservation(toTarget.magnitude / 40f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float push = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float steer = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float strafe = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);

        Vector3 force = transform.forward * push * pushAcceleration
                      + transform.right * strafe * strafeAcceleration;
        rb.AddForce(force, ForceMode.Acceleration);

        Quaternion turn = Quaternion.Euler(0f, steer * turnSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);

        float d = DistanceToTarget();
        AddReward((prevDistance - d) * progressRewardPerMeter);
        prevDistance = d;

        if (MaxStep > 0)
            AddReward(-1f / MaxStep);
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
        if (c.collider.CompareTag("Shopper"))      // ÚJ
        {
            AddReward(shopperHitPenalty);
            hitShopper = true;
            EndEpisode();
            return;
        }

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
            reachedTarget = true;                  // ÚJ
            EndEpisode();
        }
    }
}